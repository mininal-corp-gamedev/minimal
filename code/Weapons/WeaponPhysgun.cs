﻿using Sandbox;
using Sandbox.Citizen;
using Sandbox.Physics;
using System;
using System.Collections.Generic;

namespace Minimal.Weapons;

/// <summary>
/// Facepunch-style Physgun + Gravity Gun adapted for MinimalRP.
///
/// Dedicated-server rule:
/// clients only send intent and drive local viewmodel/beam feedback; the host validates
/// the active weapon, ownership and target type, then moves/freezes/launches the Rigidbody.
/// </summary>
public sealed class WeaponPhysgun : Weapon
{
    private const string HeldCollisionTag = "physgun_held";
    private const float HostMaxRangeLimit = 1024f;
    private const float HostMaxHoldDistanceLimit = 1024;
    private const float HostMaxLinearSpeedLimit = 24000f;
    private const float HostMinResponsiveLinearSpeed = 12000f;
    private const float HostMaxLaunchForceLimit = 5000f;
    private const float HostMaxPullForceLimit = 10000f;
    private const int HostMaxOverlapIterationsLimit = 12;
    private const float HostMaxAimOriginError = 220f;
    private const float HostGrabInputTimeout = 0.35f;
    private const float ShopGravityGunMaxDistance = 200f;
    private const float HostSyncedBeamSag = 48f;
    private const float HostSyncedBeamMoveBendScale = 0.035f;
    private const float HostSyncedBeamMaxBend = 140f;
    private static readonly SoundEvent DefaultDeploySound = new("weapons/common/foley/foley_deploy_weapon_01.sound");
    private static readonly SoundEvent DefaultIdleSound = new("weapons/physgun/sounds/physgun.idle.sound");

    [Property, Category("Physgun")] public float MaxRange { get; set; } = 1024f;
    [Property, Category("Physgun")] public float MinHoldDistance { get; set; } = 50f;
    [Property, Category("Physgun")] public float MaxHoldDistance { get; set; } = 1024f;
    [Property, Category("Physgun")] public float GravityGunHoldDistance { get; set; } = 0f;
    [Property, Category("Physgun")] public float PullDistance { get; set; } = 200f;
    [Property, Category("Physgun")] public float PullForce { get; set; } = 1000f;
    [Property, Category("Physgun")] public float ScrollStep { get; set; } = 20f;
    [Property, Category("Physgun")] public float MaxLinearSpeed { get; set; } = 12000f;
    [Property, Category("Physgun")] public float LaunchForce { get; set; } = 2000f;
    [Property, Category("Physgun")] public float SnapAngleDegrees { get; set; } = 45f;
    [Property, Category("Physgun")] public float RotationLerp { get; set; } = 0.5f;
    [Property, Category("Physgun")] public float SeekRadius { get; set; } = 28f;
    [Property, Category("Physgun")] public bool LocalVisualPrediction { get; set; } = true;

    [Property, Category("Physgun Beam")] public float BeamMaxLength { get; set; } = 1024f;
    [Property, Category("Physgun Beam")] public float BeamSag { get; set; } = 48f;
    [Property, Category("Physgun Beam")] public float BeamMoveBendScale { get; set; } = 0.035f;
    [Property, Category("Physgun Beam")] public float BeamMaxBend { get; set; } = 140f;
    [Property, Category("Physgun Beam")] public float BeamSeekIdleBend { get; set; } = 18f;
    [Property, Category("Physgun Beam")] public float BeamSeekNoiseSpeed { get; set; } = 5f;

    [Property, Category("Physgun Safety")] public float ReleasePlayerPadding { get; set; } = 6f;
    [Property, Category("Physgun Safety")] public float ReleasePushSpeed { get; set; } = 120f;
    [Property, Category("Physgun Safety")] public int ReleaseMaxResolveIterations { get; set; } = 6;
    [Property, Category("Physgun Safety")] public float HeldPlayerPadding { get; set; } = 12f;
    [Property, Category("Physgun Safety")] public int HeldMaxResolveIterations { get; set; } = 4;

    [Property, Category("Physgun Sounds")] public SoundEvent AttachSound { get; set; }
    [Property, Category("Physgun Sounds")] public SoundEvent ReleasedSound { get; set; }
    [Property, Category("Physgun Sounds")] public SoundEvent FreezeSound { get; set; }
    [Property, Category("Physgun Sounds")] public SoundEvent ButtonInSound { get; set; }
    [Property, Category("Physgun Sounds")] public SoundEvent ButtonOutSound { get; set; }
    [Property, Category("Physgun Sounds")] public SoundEvent DeploySound { get; set; }
    [Property, Category("Physgun Sounds")] public SoundEvent IdleSound { get; set; }

    [Property, Category("Physgun Effects")] public GameObject FreezeEffectPrefab { get; set; }
    [Property, Category("Physgun Effects")] public GameObject UnFreezeEffectPrefab { get; set; }

    protected override bool UseDefaultCombatInput => false;

    private enum GrabMode
    {
        None = 0,
        PhysgunSeek = 1,
        Physgun = 2,
        GravityGun = 3
    }

    private GrabMode _mode = GrabMode.None;
    private Rigidbody _grabbed;
    private float _grabDistance;
    private Vector3 _localOffset;
    private Vector3 _localNormal = Vector3.Up;
    private Rotation _grabOffset = Rotation.Identity;
    private int _grabSessionId;
    private int _grabInputSequence;
    private int _beamInputSequence;
    private bool _preventReselect;

    private Angles _spinSavedEyeAngles;
    private bool _spinCameraLocked;
    private bool _spinLookControlsOverridden;
    private bool _spinPreviousUseLookControls;
    private bool _spinSoundActive;

    private Vector3 _beamLastEnd;
    private Vector3 _beamBend;
    private bool _beamHasLastEnd;
    private bool _beamSeekActive;
    private bool _pullHoverActive;
    private float _attackHold;
    private Angles _lastViewmodelEyeAngles;
    private Vector2 _viewmodelInertia;
    private bool _viewmodelInertiaInitialized;
    private bool _viewmodelDeployed;
    private bool _wasGrounded = true;
    private SoundHandle _idleSoundHandle;

    private static readonly Dictionary<long, HostGrabState> HostGrabStates = new();
    private static readonly Dictionary<long, int> HostBeamSequences = new();
    private static readonly Dictionary<GameObject, long> HostHeldObjectGrabbers = new();

    public bool BeamActive => _beamHasLastEnd || _pullHoverActive || _mode == GrabMode.PhysgunSeek || _mode == GrabMode.Physgun || _mode == GrabMode.GravityGun;
    public bool PullActive => _pullHoverActive || _mode == GrabMode.GravityGun;

    private sealed class HostGrabState
    {
        public long SteamId;
        public Player Player;
        public GrabMode Mode;
        public GameObject GameObject;
        public Rigidbody Body;
        public int SessionId;
        public int LastInputSequence;
        public Vector3 LocalOffset;
        public Vector3 LocalNormal;
        public Rotation GrabOffset;
        public float GrabDistance;
        public bool HadHeldCollisionTag;
        public Vector3 AimPosition;
        public Vector3 AimForward;
        public float AimYaw;
        public float MinHoldDistance;
        public float MaxHoldDistance;
        public float MaxLinearSpeed;
        public float RotationLerp;
        public float PlayerPadding;
        public int MaxResolveIterations;
        public float ReleasePlayerPadding;
        public float ReleasePushSpeed;
        public int ReleaseMaxResolveIterations;
        public float LastInputTime;
        public Vector3 BeamLastEnd;
        public Vector3 BeamBend;
        public bool BeamHasLastEnd;
        public Sandbox.Physics.ControlJoint Joint;
        public PhysicsBody ControlBody;

        public bool IsValid()
        {
            return Player.IsValid()
                && GameObject.IsValid()
                && Body.IsValid()
                && Body.GameObject == GameObject;
        }
    }

    public static void HostReleaseForPlayer(Player player)
    {
        if (!Networking.IsHost || !player.IsValid())
            return;

        var steamId = player.GameObject.Network.Owner?.SteamId.Value ?? 0L;
        if (steamId != 0L)
            HostBeamSequences.Remove(steamId);

        if (steamId == 0L || !HostGrabStates.TryGetValue(steamId, out var state))
            return;

        HostRemoveJoint(state);

        if (state.IsValid())
        {
            HostReleaseNetworkOwnership( state.GameObject, player.GameObject.Network.Owner );
            HostResolveGrabbedPlayerOverlaps(state, preserveVelocity: false,
                padding: 6f, pushSpeed: 120f, maxIterations: 6);
            HostDisableHeldCollisionMode(state);
            state.Player.SetPhysgunBeam(false);
        }

        HostGrabStates.Remove(steamId);
    }

    public static void HostFixedUpdateForPlayer(Player player)
    {
        if (!Networking.IsHost || !player.IsValid())
            return;

        var connection = player.GameObject.Network.Owner;
        var steamId = connection?.SteamId.Value ?? 0L;
        if (steamId == 0L || !HostGrabStates.TryGetValue(steamId, out var state))
            return;

        if (!state.IsValid())
        {
            HostRemoveJoint(state);
            if (state is not null)
                HostGrabStates.Remove(state.SteamId);
            player.SetPhysgunBeam(false);
            return;
        }

        if (Time.Now - state.LastInputTime > HostGrabInputTimeout)
        {
            HostEndGrab(connection, state.GameObject, preserveVelocity: false,
                state.ReleasePlayerPadding, state.ReleasePushSpeed, state.ReleaseMaxResolveIterations);
            return;
        }

        if (!HostCanUsePhysgun(state.Player)
            || !HostCanGrabRigidbody(state.Player, state.Body, state.Mode)
            || !state.Body.MotionEnabled
            || !state.Body.PhysicsBody.IsValid())
        {
            HostEndGrab(connection, state.GameObject, preserveVelocity: false,
                state.ReleasePlayerPadding, state.ReleasePushSpeed, state.ReleaseMaxResolveIterations);
            HostRejectGrab(connection, state.GameObject);
            return;
        }

        if (!HostCanHostSimulateGrabbedBody(state))
        {
            HostUpdateSyncedBeam(state);
            return;
        }

        HostApplyGrabMovement(state);
        HostUpdateSyncedBeam(state);
    }

    protected override void OnWeaponStart()
    {
        ApplySandboxPhysgunDefaults();
        HoldType = CitizenAnimationHelper.HoldTypes.Physgun;
        Ammo = 0;
        TotalReserveAmmo = 0;
        HasReload = false;
        CanAttackWithoutAmmo = true;
        Log.Info("[WeaponPhysgun] Ready");
    }

    private void ApplySandboxPhysgunDefaults()
    {
        if (Nearly(MaxRange, 1024f)) MaxRange = 8196f;
        if (Nearly(MaxHoldDistance, 1024f)) MaxHoldDistance = 8196f;
        if (Nearly(MinHoldDistance, 60f)) MinHoldDistance = 50f;
        if (Nearly(GravityGunHoldDistance, 90f)) GravityGunHoldDistance = 0f;
        if (Nearly(ScrollStep, 25f)) ScrollStep = 20f;
        if (Nearly(LaunchForce, 1500f)) LaunchForce = 2000f;
        if (Nearly(BeamMaxLength, 1024f)) BeamMaxLength = 8196f;
    }

    protected override void OnDisabled()
    {
        if (Viewmodel != null)
            Viewmodel.Set("b_holster", true);

        RequestHostEndGrab(preserveVelocity: false);
        ResetGrab();
        StopPhysgunSounds();
        _viewmodelDeployed = false;
    }

    protected override void ResetViewmodelState()
    {
        base.ResetViewmodelState();

        if (Viewmodel == null) return;
        Viewmodel.Set("stylus", 0f);
        Viewmodel.Set("brake", 0f);
        Viewmodel.Set("b_button", false);
        Viewmodel.Set("b_attack", false);
        Viewmodel.Set("attack_hold", 0f);
        Viewmodel.Set("b_twohanded", true);
        Viewmodel.Set("b_grab", false);
        Viewmodel.Set("grab_action", 0);
        Viewmodel.Set("speed_grab", 1f);
        Viewmodel.Set("ironsights", 0);
        Viewmodel.Set("ironsights_fire_scale", 0.5f);
        Viewmodel.Set("speed_ironsights", 1f);
        if (!_viewmodelDeployed)
            Viewmodel.Set("b_deploy", true);

        Viewmodel.Set("b_holster", false);
        Viewmodel.Set("b_jump", false);
        Viewmodel.Set("b_lower_weapon", false);
        Viewmodel.Set("b_empty", false);
        Viewmodel.Set("firing_mode", 0);
        Viewmodel.Set("move_speed", 0f);
        Viewmodel.Set("move_groundspeed", 0f);

        if (!_viewmodelDeployed)
        {
            PlayLocalSound(DeploySound, DefaultDeploySound);
            _viewmodelDeployed = true;
        }
    }

    protected override void OnWeaponFixedUpdate()
    {
        UpdateViewmodelState();
        ApplyOwnedClientGrabMovement();
    }

    protected override void OnWeaponUpdate()
    {
        if (!Player.Local.IsValid()) return;

        if (Player.Local.IsArrested || !Player.Local.IsAlive)
        {
            RequestHostEndGrab(preserveVelocity: false);
            ResetGrab();
            return;
        }

        if (!Player.Local.Controller.IsValid()) return;

        ValidateGrabbed();
        HandleInput();
        UpdateSpin();
        UpdateLocalGrabPrediction();
        UpdateGrabbed();
        UpdateBeamState();
        UpdateSoundState();
        UpdateViewmodelState();
    }

    private void ValidateGrabbed()
    {
        if (_mode == GrabMode.None || _mode == GrabMode.PhysgunSeek)
            return;

        if (!_grabbed.IsValid() || !_grabbed.GameObject.IsValid())
        {
            RequestHostEndGrab(preserveVelocity: false);
            ResetGrab();
        }
    }

    private void HandleInput()
    {
        _pullHoverActive = false;

        bool lmbDown = Input.Down("Attack1");
        bool rmbDown = Input.Down("Attack2");
        bool lmbPressed = Input.Pressed("Attack1");
        bool rmbPressed = Input.Pressed("Attack2");

        if (_preventReselect)
        {
            if (!lmbDown && !rmbDown)
                _preventReselect = false;
            return;
        }

        if (_mode == GrabMode.None)
        {
            if (Input.Pressed("Reload"))
            {
                RequestHostUnfreezeAllAtAim();
                return;
            }

            if (rmbDown)
            {
                HandleGravityPull();
                return;
            }

            if (lmbPressed)
            {
                BeginPhysgunSeek();
                return;
            }

            return;
        }

        if (_mode == GrabMode.PhysgunSeek)
        {
            if (!lmbDown)
                EndSeek();
            else
                TryStartGrab(GrabMode.Physgun);

            return;
        }

        if (_mode == GrabMode.Physgun)
        {
            if (rmbPressed)
            {
                RequestHostFreezeGrab();
                EndGrab();
                return;
            }

            if (!lmbDown)
            {
                EndGrab();
                return;
            }

            var wheelY = Input.MouseWheel.y;
            if (MathF.Abs(wheelY) > 0.001f)
            {
                _grabDistance = Clamp(_grabDistance + wheelY * ScrollStep, MinHoldDistance, MaxHoldDistance);
                Input.MouseWheel = Vector2.Zero;
            }

            return;
        }

        if (_mode == GrabMode.GravityGun)
        {
            if (lmbPressed)
            {
                RequestHostLaunchGrab();
                EndGrab(preserveVelocity: true);
                return;
            }

            if (!rmbDown)
                EndGrab();
        }
    }

    private void HandleGravityPull()
    {
        var player = Player.Local;
        if (!player.IsValid() || !player.Controller.IsValid())
            return;

        var eye = player.Controller.EyeTransform;
        if (!TryFindGrabTarget(GrabMode.GravityGun, eye.Position, eye.Forward, out _, out var rb))
            return;

        _pullHoverActive = true;

        if (!rb.MotionEnabled)
            return;

        var closest = rb.FindClosestPoint(eye.Position);
        var distance = Vector3.DistanceBetween(closest, eye.Position);

        if (distance <= MathF.Max(0f, PullDistance))
        {
            TryStartGrab(GrabMode.GravityGun);
            return;
        }

        RequestHostPullAtAim(eye.Position, eye.Forward, player.Controller.EyeAngles.yaw);
    }

    private void BeginPhysgunSeek()
    {
        _mode = GrabMode.PhysgunSeek;
        _beamSeekActive = true;
        TryStartGrab(GrabMode.Physgun);
    }

    private void EndSeek()
    {
        RequestHostEndGrab(preserveVelocity: false);
        ResetGrab();
        _preventReselect = true;
    }

    private bool TryStartGrab(GrabMode mode)
    {
        var eye = Player.Local.Controller.EyeTransform;
        var origin = eye.Position;
        var dir = eye.Forward;

        if (!TryFindGrabTarget(mode, origin, dir, out var tr, out var rb))
            return false;

        var sessionId = unchecked(++_grabSessionId);
        if (sessionId == 0)
            sessionId = _grabSessionId = 1;

        _grabInputSequence = 0;

        StartLocalGrab(mode, eye, tr, rb);
        RequestHostStartGrab(mode, origin, dir, Player.Local.Controller.EyeAngles.yaw, sessionId);
        return true;
    }

    private void StartLocalGrab(GrabMode mode, Transform eye, SceneTraceResult tr, Rigidbody rb)
    {
        _grabbed = rb;
        _mode = mode;
        _beamSeekActive = false;

        var bodyTransform = GetTraceBodyTransform(tr, rb);
        _localNormal = bodyTransform.NormalToLocal(tr.Normal);

        if (mode == GrabMode.GravityGun)
        {
            _localOffset = GetPullLocalOffset(rb, GetTraceBodyTransform(tr, rb));
            _grabDistance = GravityGunHoldDistance;
            _grabOffset = eye.Rotation.Inverse * bodyTransform.Rotation;
        }
        else
        {
            _localOffset = bodyTransform.PointToLocal(tr.HitPosition);
            var hitDistance = Vector3.DistanceBetween(eye.Position, tr.HitPosition);
            _grabDistance = Clamp(hitDistance, MinHoldDistance, MaxHoldDistance);
            var yaw = Rotation.FromYaw(Player.Local.Controller.EyeAngles.yaw);
            _grabOffset = yaw.Inverse * bodyTransform.Rotation;
        }
    }

    private void EndGrab(bool preserveVelocity = false)
    {
        RequestHostEndGrab(preserveVelocity);
        ResetGrab();
        _preventReselect = true;
        PlayLocalSound(ReleasedSound);
    }

    private void ResetGrab()
    {
        _mode = GrabMode.None;
        _grabbed = null;
        _grabDistance = 0f;
        _grabOffset = Rotation.Identity;
        _localOffset = Vector3.Zero;
        _localNormal = Vector3.Up;
        _beamSeekActive = false;
        _pullHoverActive = false;
        ResetBeamState();
        UnlockSpinCamera();
        SetSpinSoundActive(false);
    }

    private bool TryFindGrabTarget(GrabMode mode, Vector3 origin, Vector3 dir, out SceneTraceResult tr, out Rigidbody rb)
    {
        tr = TraceGrabRay(origin, dir, 0f);
        if (TryGetGrabRigidbody(tr, mode, out rb))
            return true;

        if (tr.Hit || mode != GrabMode.Physgun || SeekRadius <= 0f)
        {
            rb = null;
            return false;
        }

        tr = TraceGrabRay(origin, dir, SeekRadius);
        return TryGetGrabRigidbody(tr, mode, out rb);
    }

    private SceneTraceResult TraceGrabRay(Vector3 origin, Vector3 dir, float radius)
    {
        return Scene.Trace
            .Ray(origin, origin + dir * MaxRange)
            .Radius(MathF.Max(0f, radius))
            .IgnoreGameObjectHierarchy(Player.Local.GameObject)
            .WithoutTags("bullet", "player")
            .Run();
    }

    private bool TryGetGrabRigidbody(SceneTraceResult tr, GrabMode mode, out Rigidbody rb)
    {
        rb = null;

        if (!tr.Hit || !tr.GameObject.IsValid())
            return false;

        rb = tr.GameObject.Components.Get<Rigidbody>(FindMode.EverythingInSelfAndAncestors);
        if (!rb.IsValid())
            return false;

        return LocalPlayerCanGrab(rb, mode);
    }

    private static bool LocalPlayerCanGrab(Rigidbody rb, GrabMode mode)
    {
        var player = Player.Local;
        return player.IsValid() && HostCanGrabRigidbody(player, rb, mode);
    }

    private void UpdateSpin()
    {
        if (!Player.Local.IsValid() || !Player.Local.Controller.IsValid())
        {
            UnlockSpinCamera();
            SetSpinSoundActive(false);
            return;
        }

        var spinDown = Input.Down("use") || Input.Down("Use") || Input.Down("Reload");
        var spinPressed = Input.Pressed("use") || Input.Pressed("Use") || Input.Pressed("Reload");

        bool canSpin = _mode == GrabMode.Physgun
            && _grabbed.IsValid()
            && spinDown;

        SetSpinSoundActive(canSpin);

        if (!canSpin)
        {
            UnlockSpinCamera();
            return;
        }

        var controller = Player.Local.Controller;
        if (!_spinCameraLocked || spinPressed)
        {
            _spinSavedEyeAngles = controller.EyeAngles;
            _spinCameraLocked = true;
        }

        LockSpinCamera(controller);
        if (Input.Down("use") || Input.Down("Use"))
            Input.Clear("use");

        bool snapping = Input.Down("Run") || Input.Down("Walk") || Input.Down("walk");
        var snapAngle = Input.Down("Walk") || Input.Down("walk") ? 15f : SnapAngleDegrees;
        var look = Input.AnalogLook * -1f;

        if (snapping)
        {
            if (MathF.Abs(look.yaw) > MathF.Abs(look.pitch)) look.pitch = 0;
            else look.yaw = 0;
        }

        var spinRotation = Rotation.From(look) * _grabOffset;

        if (snapping)
        {
            var eyeYaw = Rotation.FromYaw(_spinSavedEyeAngles.yaw);
            var spinWorld = eyeYaw * spinRotation;
            var snapped = spinWorld.Angles().SnapToGrid(snapAngle);
            spinRotation = eyeYaw.Inverse * Rotation.From(snapped);
        }

        _grabOffset = spinRotation;
        controller.EyeAngles = _spinSavedEyeAngles;
        Input.AnalogLook = default;
    }

    private void LockSpinCamera(PlayerController controller)
    {
        if (!controller.IsValid() || _spinLookControlsOverridden) return;

        _spinPreviousUseLookControls = controller.UseLookControls;
        controller.UseLookControls = false;
        _spinLookControlsOverridden = true;
    }

    private void UnlockSpinCamera()
    {
        if (_spinLookControlsOverridden && Player.Local.IsValid() && Player.Local.Controller.IsValid())
            Player.Local.Controller.UseLookControls = _spinPreviousUseLookControls;

        _spinLookControlsOverridden = false;
        _spinCameraLocked = false;
    }

    private void SetSpinSoundActive(bool active)
    {
        if (_spinSoundActive == active) return;

        _spinSoundActive = active;
        PlayLocalSound(active ? ButtonInSound : ButtonOutSound);
    }

    private void UpdateGrabbed()
    {
        if (_mode == GrabMode.None || _mode == GrabMode.PhysgunSeek || !_grabbed.IsValid())
            return;

        var eye = Player.Local.Controller.EyeTransform;
        var yaw = Player.Local.Controller.EyeAngles.yaw;
        var inputSequence = unchecked(++_grabInputSequence);

        if (Networking.IsHost)
        {
            HostUpdateGrab(GetLocalPlayerConnection(), _grabbed.GameObject, _grabDistance, _grabOffset,
                _grabSessionId, inputSequence,
                eye.Position, eye.Forward, yaw,
                MinHoldDistance, MaxHoldDistance, MaxLinearSpeed, RotationLerp,
                ReleasePlayerPadding, HeldPlayerPadding, HeldMaxResolveIterations);
            return;
        }

        RpcHostUpdateGrab(_grabbed.GameObject, _grabDistance, _grabOffset,
            _grabSessionId, inputSequence,
            eye.Position, eye.Forward, yaw,
            MinHoldDistance, MaxHoldDistance, MaxLinearSpeed, RotationLerp,
            ReleasePlayerPadding, HeldPlayerPadding, HeldMaxResolveIterations);
    }

    private void UpdateLocalGrabPrediction()
    {
        if (!LocalVisualPrediction || Networking.IsHost)
            return;

        if (_mode == GrabMode.None || _mode == GrabMode.PhysgunSeek || !_grabbed.IsValid())
            return;

        var player = Player.Local;
        if (!player.IsValid() || !player.Controller.IsValid())
            return;

        var eye = player.Controller.EyeTransform;
        var grabDistance = HostClampGrabDistance(_grabbed, GetBeamEndPosition(), eye,
            _grabDistance, MinHoldDistance, MaxHoldDistance);
        var targetPos = eye.Position + eye.Forward * grabDistance;

        var targetRot = _mode == GrabMode.GravityGun
            ? eye.Rotation * _grabOffset
            : Rotation.FromYaw(player.Controller.EyeAngles.yaw) * _grabOffset;

        _grabbed.WorldRotation = targetRot;
        _grabbed.WorldPosition = targetPos - targetRot * _localOffset;
    }

    private void ApplyOwnedClientGrabMovement()
    {
        if (Networking.IsHost)
            return;

        if (_mode == GrabMode.None || _mode == GrabMode.PhysgunSeek || !_grabbed.IsValid())
            return;

        if (_grabbed.IsProxy)
            return;

        var player = Player.Local;
        if (!player.IsValid() || !player.Controller.IsValid())
            return;

        var eye = player.Controller.EyeTransform;
        var grabDistance = HostClampGrabDistance(_grabbed, GetBeamEndPosition(), eye,
            _grabDistance, MinHoldDistance, MaxHoldDistance);
        var targetPos = eye.Position + eye.Forward * grabDistance;

        var targetRot = _mode == GrabMode.GravityGun
            ? eye.Rotation * _grabOffset
            : Rotation.FromYaw(player.Controller.EyeAngles.yaw) * _grabOffset;

        var desiredBodyPos = targetPos - targetRot * _localOffset;
        _grabbed.WorldRotation = targetRot;
        _grabbed.WorldPosition = desiredBodyPos;
        _grabbed.Velocity = Vector3.Zero;
        _grabbed.AngularVelocity = Vector3.Zero;
    }

    private void RequestHostStartGrab(GrabMode mode, Vector3 origin, Vector3 forward, float yaw, int sessionId)
    {
        if (Networking.IsHost)
        {
            HostStartGrab(GetLocalPlayerConnection(), (int)mode, origin, forward, yaw, sessionId,
                MaxRange, MinHoldDistance, MaxHoldDistance, GravityGunHoldDistance, SeekRadius, AttachSound,
                UnFreezeEffectPrefab);
            return;
        }

        RpcHostStartGrab((int)mode, origin, forward, yaw, sessionId,
            MaxRange, MinHoldDistance, MaxHoldDistance, GravityGunHoldDistance, SeekRadius, AttachSound,
            UnFreezeEffectPrefab);
    }

    private void RequestHostPullAtAim(Vector3 origin, Vector3 forward, float yaw)
    {
        if (Networking.IsHost)
        {
            HostPullAtAim(GetLocalPlayerConnection(), origin, forward, yaw,
                MaxRange, PullDistance, PullForce, SeekRadius);
            return;
        }

        RpcHostPullAtAim(origin, forward, yaw, MaxRange, PullDistance, PullForce, SeekRadius);
    }

    private void RequestHostEndGrab(bool preserveVelocity)
    {
        if (!_grabbed.IsValid())
            return;

        if (Networking.IsHost)
        {
            HostEndGrab(GetLocalPlayerConnection(), _grabbed.GameObject, preserveVelocity,
                ReleasePlayerPadding, ReleasePushSpeed, ReleaseMaxResolveIterations);
            return;
        }

        RpcHostEndGrab(_grabbed.GameObject, preserveVelocity,
            ReleasePlayerPadding, ReleasePushSpeed, ReleaseMaxResolveIterations);
    }

    private void RequestHostFreezeGrab()
    {
        if (!_grabbed.IsValid())
            return;

        if (Networking.IsHost)
        {
            HostFreezeGrab(GetLocalPlayerConnection(), _grabbed.GameObject,
                ReleasePlayerPadding, ReleasePushSpeed, ReleaseMaxResolveIterations, FreezeSound, FreezeEffectPrefab);
            return;
        }

        RpcHostFreezeGrab(_grabbed.GameObject,
            ReleasePlayerPadding, ReleasePushSpeed, ReleaseMaxResolveIterations, FreezeSound, FreezeEffectPrefab);
    }

    private void RequestHostUnfreezeAllAtAim()
    {
        var player = Player.Local;
        if (!player.IsValid() || !player.Controller.IsValid())
            return;

        var eye = player.Controller.EyeTransform;
        if (Networking.IsHost)
        {
            HostUnfreezeAllAtAim(GetLocalPlayerConnection(), eye.Position, eye.Forward, MaxRange, UnFreezeEffectPrefab);
            return;
        }

        RpcHostUnfreezeAllAtAim(eye.Position, eye.Forward, MaxRange, UnFreezeEffectPrefab);
    }

    private void RequestHostLaunchGrab()
    {
        if (!_grabbed.IsValid())
            return;

        if (Networking.IsHost)
        {
            HostLaunchGrab(GetLocalPlayerConnection(), _grabbed.GameObject, LaunchForce,
                ReleasePlayerPadding, ReleasePushSpeed, ReleaseMaxResolveIterations);
            return;
        }

        RpcHostLaunchGrab(_grabbed.GameObject, LaunchForce,
            ReleasePlayerPadding, ReleasePushSpeed, ReleaseMaxResolveIterations);
    }

    private Connection GetLocalPlayerConnection()
    {
        return Player.Local?.GameObject.Network.Owner ?? Connection.Local;
    }

    [Rpc.Host]
    private static void RpcHostStartGrab(int mode, Vector3 origin, Vector3 forward, float yaw, int sessionId,
        float maxRange, float minHoldDistance, float maxHoldDistance, float gravityGunHoldDistance,
        float seekRadius, SoundEvent attachSound, GameObject unfreezeEffectPrefab)
    {
#if SERVER
        if (!Networking.IsHost) return;
        HostStartGrab(Rpc.Caller, mode, origin, forward, yaw, sessionId, maxRange, minHoldDistance,
            maxHoldDistance, gravityGunHoldDistance, seekRadius, attachSound, unfreezeEffectPrefab);
#endif
    }

    [Rpc.Host(NetFlags.UnreliableNoDelay)]
    private static void RpcHostPullAtAim(Vector3 origin, Vector3 forward, float yaw,
        float maxRange, float pullDistance, float pullForce, float seekRadius)
    {
#if SERVER
        if (!Networking.IsHost) return;
        HostPullAtAim(Rpc.Caller, origin, forward, yaw, maxRange, pullDistance, pullForce, seekRadius);
#endif
    }

    [Rpc.Host(NetFlags.UnreliableNoDelay)]
    private static void RpcHostUpdateGrab(GameObject target, float grabDistance, Rotation grabOffset,
        int sessionId, int inputSequence,
        Vector3 aimPosition, Vector3 aimForward, float aimYaw,
        float minHoldDistance, float maxHoldDistance, float maxLinearSpeed, float rotationLerp,
        float releasePlayerPadding, float heldPlayerPadding, int heldMaxResolveIterations)
    {
#if SERVER
        if (!Networking.IsHost) return;
        HostUpdateGrab(Rpc.Caller, target, grabDistance, grabOffset,
            sessionId, inputSequence,
            aimPosition, aimForward, aimYaw,
            minHoldDistance, maxHoldDistance, maxLinearSpeed, rotationLerp,
            releasePlayerPadding, heldPlayerPadding, heldMaxResolveIterations);
#endif
    }

    [Rpc.Host]
    private static void RpcHostEndGrab(GameObject target, bool preserveVelocity,
        float releasePlayerPadding, float releasePushSpeed, int releaseMaxResolveIterations)
    {
#if SERVER
        if (!Networking.IsHost) return;
        HostEndGrab(Rpc.Caller, target, preserveVelocity, releasePlayerPadding,
            releasePushSpeed, releaseMaxResolveIterations);
#endif
    }

    [Rpc.Host]
    private static void RpcHostFreezeGrab(GameObject target,
        float releasePlayerPadding, float releasePushSpeed, int releaseMaxResolveIterations,
        SoundEvent freezeSound, GameObject freezeEffectPrefab)
    {
#if SERVER
        if (!Networking.IsHost) return;
        HostFreezeGrab(Rpc.Caller, target, releasePlayerPadding,
            releasePushSpeed, releaseMaxResolveIterations, freezeSound, freezeEffectPrefab);
#endif
    }

    [Rpc.Host]
    private static void RpcHostUnfreezeAllAtAim(Vector3 origin, Vector3 forward, float maxRange, GameObject unfreezeEffectPrefab)
    {
#if SERVER
        if (!Networking.IsHost) return;
        HostUnfreezeAllAtAim(Rpc.Caller, origin, forward, maxRange, unfreezeEffectPrefab);
#endif
    }

    [Rpc.Host]
    private static void RpcHostLaunchGrab(GameObject target, float launchForce,
        float releasePlayerPadding, float releasePushSpeed, int releaseMaxResolveIterations)
    {
#if SERVER
        if (!Networking.IsHost) return;
        HostLaunchGrab(Rpc.Caller, target, launchForce, releasePlayerPadding,
            releasePushSpeed, releaseMaxResolveIterations);
#endif
    }

    [Rpc.Host(NetFlags.UnreliableNoDelay)]
    private static void RpcHostUpdatePhysgunBeam(int sequence, Vector3 start, Vector3 end, Vector3 bend,
        Vector3 endNormal, bool grabbed, float maxRange)
    {
#if SERVER
        if (!Networking.IsHost) return;
        if (!TryGetCallerPlayer(Rpc.Caller, out var player)) return;
        if (!HostCanUsePhysgun(player)) return;
        if (!HostAcceptBeamSequence(Rpc.Caller, sequence)) return;

        if (HostGrabStates.ContainsKey(Rpc.Caller.SteamId.Value))
            return;

        var safeStart = HostValidateBeamStart(player, start, end);
        var safeEnd = HostValidateBeamEnd(safeStart, end, maxRange);
        var safeBend = ClampVectorLength(bend, HostSyncedBeamMaxBend);
        var safeNormal = IsFiniteVector(endNormal) && endNormal.LengthSquared > 0.001f ? endNormal.Normal : Vector3.Up;

        player.SetPhysgunBeam(true, safeStart, safeEnd, safeBend, safeNormal, grabbed: false);
#endif
    }

    [Rpc.Host]
    private static void RpcHostClearPhysgunBeam(int sequence)
    {
#if SERVER
        if (!Networking.IsHost) return;
        if (!TryGetCallerPlayer(Rpc.Caller, out var player)) return;
        if (!HostAcceptBeamSequence(Rpc.Caller, sequence)) return;

        if (HostGrabStates.ContainsKey(Rpc.Caller.SteamId.Value))
            return;

        player.SetPhysgunBeam(false);
#endif
    }

    private static void HostPullAtAim(Connection caller, Vector3 origin, Vector3 forward, float yaw,
        float maxRange, float pullDistance, float pullForce, float seekRadius)
    {
        if (!TryGetCallerPlayer(caller, out var player))
            return;

        if (!HostCanUsePhysgun(player))
            return;

        if (HostGrabStates.ContainsKey(caller.SteamId.Value))
            return;

        var aim = HostGetValidatedAimTransform(player, origin, forward, yaw);
        maxRange = Clamp(FiniteOrDefault(maxRange, 8196f), 64f, HostMaxRangeLimit);
        pullDistance = Clamp(FiniteOrDefault(pullDistance, 200f), 0f, HostMaxRangeLimit);
        pullForce = Clamp(FiniteOrDefault(pullForce, 1000f), 0f, HostMaxPullForceLimit);
        seekRadius = Clamp(FiniteOrDefault(seekRadius, 0f), 0f, 64f);

        if (!HostTryFindGrabTarget(player, GrabMode.GravityGun, aim, maxRange, seekRadius, out var tr, out var rb))
            return;

        if (!HostCanGrabRigidbody(player, rb, GrabMode.GravityGun))
            return;

        if (IsHeldByAnotherPlayer(rb.GameObject, caller.SteamId.Value))
            return;

        if (IsShopGravityGunTarget(rb) && !HostIsWithinShopGravityGunDistance(player, rb))
            return;

        if (!HostCanMoveBody(rb))
            return;

        var closest = rb.FindClosestPoint(aim.Position);
        if (Vector3.DistanceBetween(closest, aim.Position) <= pullDistance)
            return;

        var bodyTransform = GetTraceBodyTransform(tr, rb);
        var localOffset = GetPullLocalOffset(rb, bodyTransform);
        var endPoint = bodyTransform.PointToWorld(localOffset);
        var force = aim.Rotation.Backward * rb.Mass * pullForce;

        rb.ApplyForceAt(endPoint, force);
    }

    private static void HostStartGrab(Connection caller, int modeValue, Vector3 origin, Vector3 forward, float yaw, int sessionId,
        float maxRange, float minHoldDistance, float maxHoldDistance, float gravityGunHoldDistance,
        float seekRadius, SoundEvent attachSound, GameObject unfreezeEffectPrefab)
    {
        if (!TryGetCallerPlayer(caller, out var player))
            return;

        if (!HostCanUsePhysgun(player))
        {
            HostRejectGrab(caller, null);
            return;
        }

        var mode = modeValue == (int)GrabMode.GravityGun ? GrabMode.GravityGun : GrabMode.Physgun;
        var aim = HostGetValidatedAimTransform(player, origin, forward, yaw);

        maxRange = Clamp(FiniteOrDefault(maxRange, 8196f), 64f, HostMaxRangeLimit);
        minHoldDistance = Clamp(FiniteOrDefault(minHoldDistance, 50f), 1f, HostMaxHoldDistanceLimit);
        maxHoldDistance = Clamp(FiniteOrDefault(maxHoldDistance, 8196f), minHoldDistance, HostMaxHoldDistanceLimit);
        gravityGunHoldDistance = Clamp(FiniteOrDefault(gravityGunHoldDistance, 0f), 0f, maxHoldDistance);
        seekRadius = Clamp(FiniteOrDefault(seekRadius, 0f), 0f, 64f);

        if (!HostTryFindGrabTarget(player, mode, aim, maxRange, seekRadius, out var tr, out var rb))
        {
            HostRejectGrab(caller, null);
            return;
        }

        if (!rb.PhysicsBody.IsValid())
        {
            HostRejectGrab(caller, rb.GameObject);
            return;
        }

        if (!HostCanGrabRigidbody(player, rb, mode))
        {
            HostRejectGrab(caller, rb.GameObject);
            return;
        }

        if (IsHeldByAnotherPlayer(rb.GameObject, caller.SteamId.Value))
        {
            HostRejectGrab(caller, rb.GameObject);
            return;
        }

        if (mode == GrabMode.GravityGun && IsShopGravityGunTarget(rb) && !HostIsWithinShopGravityGunDistance(player, rb))
        {
            HostRejectGrab(caller, rb.GameObject);
            return;
        }

        HostEndGrab(caller, null, preserveVelocity: false, 6f, 120f, 6);

        if (!rb.MotionEnabled && mode == GrabMode.GravityGun)
        {
            HostRejectGrab(caller, rb.GameObject);
            return;
        }

        if (!rb.MotionEnabled)
        {
            HostBroadcastPhysgunEffect(unfreezeEffectPrefab, rb.GameObject, rb.WorldTransform);
            rb.MotionEnabled = true;
        }

        var bodyTransform = GetTraceBodyTransform(tr, rb);
        var state = new HostGrabState
        {
            SteamId = caller.SteamId.Value,
            Player = player,
            Mode = mode,
            GameObject = rb.GameObject,
            Body = rb,
            SessionId = sessionId,
            LastInputSequence = 0,
            HadHeldCollisionTag = rb.GameObject.Tags.Has(HeldCollisionTag),
            LocalNormal = bodyTransform.NormalToLocal(tr.Normal),
            AimPosition = aim.Position,
            AimForward = aim.Forward,
            AimYaw = aim.Rotation.Yaw(),
            MinHoldDistance = minHoldDistance,
            MaxHoldDistance = maxHoldDistance,
            MaxLinearSpeed = HostMinResponsiveLinearSpeed,
            RotationLerp = 0.5f,
            PlayerPadding = 12f,
            MaxResolveIterations = 4,
            ReleasePlayerPadding = 6f,
            ReleasePushSpeed = 120f,
            ReleaseMaxResolveIterations = 6,
            LastInputTime = Time.Now
        };

        if (mode == GrabMode.GravityGun)
        {
            state.LocalOffset = GetPullLocalOffset(rb, bodyTransform);
            state.GrabDistance = gravityGunHoldDistance;
            state.GrabOffset = aim.Rotation.Inverse * bodyTransform.Rotation;

            rb.Velocity = Vector3.Zero;
            rb.AngularVelocity = Vector3.Zero;
        }
        else
        {
            state.LocalOffset = bodyTransform.PointToLocal(tr.HitPosition);
            state.GrabDistance = Vector3.DistanceBetween(aim.Position, tr.HitPosition);
            state.GrabDistance = HostClampGrabDistance(rb, tr.HitPosition, aim, state.GrabDistance, minHoldDistance, maxHoldDistance);
            state.GrabOffset = Rotation.FromYaw(aim.Rotation.Yaw()).Inverse * bodyTransform.Rotation;
        }

        rb.GameObject.Tags.Add(HeldCollisionTag);
        rb.GameObject.Network.AssignOwnership(caller);
        HostRegisterHeldObject(rb.GameObject, caller.SteamId.Value);
        HostGrabStates[state.SteamId] = state;
        RpcBroadcastPhysgunSound(attachSound, tr.HitPosition);
    }

    private static void HostUpdateGrab(Connection caller, GameObject target, float grabDistance, Rotation grabOffset,
        int sessionId, int inputSequence,
        Vector3 aimPosition, Vector3 aimForward, float aimYaw,
        float minHoldDistance, float maxHoldDistance, float maxLinearSpeed, float rotationLerp,
        float releasePlayerPadding, float heldPlayerPadding, int heldMaxResolveIterations)
    {
        if (!TryGetHostState(caller, target, out var state))
            return;

        if (state.SessionId != sessionId || inputSequence <= state.LastInputSequence)
            return;

        if (!HostCanUsePhysgun(state.Player)
            || !HostCanGrabRigidbody(state.Player, state.Body, state.Mode)
            || !state.Body.MotionEnabled
            || !state.Body.PhysicsBody.IsValid())
        {
            HostEndGrab(caller, target, preserveVelocity: false, releasePlayerPadding, 120f, 6);
            HostRejectGrab(caller, target);
            return;
        }

        if (state.Mode == GrabMode.GravityGun && IsShopGravityGunTarget(state.Body)
            && !HostIsWithinShopGravityGunDistance(state.Player, state.Body))
        {
            HostEndGrab(caller, target, preserveVelocity: false, releasePlayerPadding, 120f, 6);
            HostRejectGrab(caller, target);
            return;
        }

        minHoldDistance = Clamp(FiniteOrDefault(minHoldDistance, state.MinHoldDistance), 1f, HostMaxHoldDistanceLimit);
        maxHoldDistance = Clamp(FiniteOrDefault(maxHoldDistance, state.MaxHoldDistance), minHoldDistance, HostMaxHoldDistanceLimit);
        maxLinearSpeed = MathF.Max(Clamp(FiniteOrDefault(maxLinearSpeed, state.MaxLinearSpeed), 100f, HostMaxLinearSpeedLimit), HostMinResponsiveLinearSpeed);
        rotationLerp = Clamp(FiniteOrDefault(rotationLerp, state.RotationLerp), 0.01f, 1f);
        heldPlayerPadding = Clamp(FiniteOrDefault(heldPlayerPadding, state.PlayerPadding), 0f, 64f);
        releasePlayerPadding = Clamp(FiniteOrDefault(releasePlayerPadding, state.ReleasePlayerPadding), 0f, 64f);
        heldMaxResolveIterations = Math.Clamp(heldMaxResolveIterations, 1, HostMaxOverlapIterationsLimit);

        var aim = HostGetValidatedAimTransform(state.Player, aimPosition, aimForward, aimYaw);

        state.GrabDistance = Clamp(FiniteOrDefault(grabDistance, state.GrabDistance), minHoldDistance, maxHoldDistance);
        state.GrabOffset = grabOffset;
        state.LastInputSequence = inputSequence;
        state.AimPosition = aim.Position;
        state.AimForward = aim.Forward;
        state.AimYaw = aim.Rotation.Yaw();
        state.MinHoldDistance = minHoldDistance;
        state.MaxHoldDistance = maxHoldDistance;
        state.MaxLinearSpeed = maxLinearSpeed;
        state.RotationLerp = rotationLerp;
        state.PlayerPadding = MathF.Max(releasePlayerPadding, heldPlayerPadding);
        state.MaxResolveIterations = heldMaxResolveIterations;
        state.ReleasePlayerPadding = releasePlayerPadding;
        state.ReleasePushSpeed = 120f;
        state.ReleaseMaxResolveIterations = 6;
        state.LastInputTime = Time.Now;
    }

    private static void HostEndGrab(Connection caller, GameObject target, bool preserveVelocity,
        float releasePlayerPadding, float releasePushSpeed, int releaseMaxResolveIterations)
    {
        if (!TryGetHostState(caller, target, out var state))
            return;

        releasePlayerPadding = Clamp(releasePlayerPadding, 0f, 64f);
        releasePushSpeed = Clamp(releasePushSpeed, 0f, 1000f);
        releaseMaxResolveIterations = Math.Clamp(releaseMaxResolveIterations, 1, HostMaxOverlapIterationsLimit);

        HostRemoveJoint(state);
        HostReleaseNetworkOwnership(state.GameObject, caller);
        HostResolveGrabbedPlayerOverlaps(state, preserveVelocity, releasePlayerPadding,
            releasePushSpeed, releaseMaxResolveIterations);
        HostDisableHeldCollisionMode(state);
        state.Player.SetPhysgunBeam(false);
        HostGrabStates.Remove(state.SteamId);

        if (state.Body.IsValid() && !state.Body.IsProxy && TryGetPropCustom(state.Body, out _))
        {
            state.Body.Velocity = Vector3.Zero;
            state.Body.AngularVelocity = Vector3.Zero;
            state.Body.MotionEnabled = false;
        }
    }

    private static void HostFreezeGrab(Connection caller, GameObject target,
        float releasePlayerPadding, float releasePushSpeed, int releaseMaxResolveIterations,
        SoundEvent freezeSound, GameObject freezeEffectPrefab)
    {
        if (!TryGetHostState(caller, target, out var state))
            return;

        var body = state.Body;
        HostEndGrab(caller, target, preserveVelocity: false, releasePlayerPadding,
            releasePushSpeed, releaseMaxResolveIterations);

        if (!body.IsValid() || body.IsProxy)
            return;

        body.Velocity = Vector3.Zero;
        body.AngularVelocity = Vector3.Zero;
        body.MotionEnabled = false;
        HostBroadcastPhysgunEffect(freezeEffectPrefab, body.GameObject, body.WorldTransform);

        if (!freezeEffectPrefab.IsValid())
            RpcBroadcastPhysgunSound(freezeSound, body.WorldPosition);
    }

    private static void HostUnfreezeAllAtAim(Connection caller, Vector3 origin, Vector3 forward,
        float maxRange, GameObject unfreezeEffectPrefab)
    {
        if (!TryGetCallerPlayer(caller, out var player))
            return;

        if (!HostCanUsePhysgun(player))
            return;

        var aim = HostGetValidatedAimTransform(player, origin, forward, player.Controller.EyeAngles.yaw);
        maxRange = Clamp(FiniteOrDefault(maxRange, 8196f), 64f, HostMaxRangeLimit);
        var tr = HostTraceGrabRay(player, aim.Position, aim.Forward, maxRange, 0f);

        if (!HostTryGetGrabRigidbody(player, tr, GrabMode.Physgun, out var body))
            return;

        if (!body.IsValid() || body.IsProxy)
            return;

        HostUnfreezeConnectedBodies(body, unfreezeEffectPrefab);
    }

    private static void HostUnfreezeConnectedBodies(Rigidbody body, GameObject unfreezeEffectPrefab)
    {
        if (!body.IsValid() || body.IsProxy)
            return;

        var bodies = new HashSet<Rigidbody>();
        GetConnectedBodies(body.GameObject, bodies);

        HostBroadcastPhysgunEffect(unfreezeEffectPrefab, body.GameObject, body.WorldTransform);

        foreach (var rb in bodies)
        {
            if (!rb.IsValid() || rb.IsProxy)
                continue;

            if (TryGetPropCustom(rb, out _))
                continue;

            rb.MotionEnabled = true;
        }
    }

    private static void HostLaunchGrab(Connection caller, GameObject target, float launchForce,
        float releasePlayerPadding, float releasePushSpeed, int releaseMaxResolveIterations)
    {
        if (!TryGetHostState(caller, target, out var state))
            return;

        var body = state.Body;
        if (!body.IsValid() || body.IsProxy)
        {
            HostEndGrab(caller, target, preserveVelocity: false, releasePlayerPadding,
                releasePushSpeed, releaseMaxResolveIterations);
            return;
        }

        HostRemoveJoint(state);

        var dir = state.Player.Controller.IsValid()
            ? state.Player.Controller.EyeTransform.Forward
            : state.Player.WorldRotation.Forward;

        launchForce = Clamp(launchForce, 0f, HostMaxLaunchForceLimit);
        var mass = MathF.Max(0.1f, body.Mass);
        body.ApplyImpulse(dir.Normal * (launchForce * mass));
        body.PhysicsBody?.ApplyAngularImpulse(Vector3.Random * (launchForce * mass));

        HostEndGrab(caller, target, preserveVelocity: true, releasePlayerPadding,
            releasePushSpeed, releaseMaxResolveIterations);
    }

    private static bool TryGetCallerPlayer(Connection caller, out Player player)
    {
        player = null;
        if (caller is null)
            return false;

        player = Player.FindPlayerBySteamId(caller.SteamId.Value);
        return player.IsValid();
    }

    private static bool HostAcceptBeamSequence(Connection caller, int sequence)
    {
        if (caller is null)
            return false;

        var steamId = caller.SteamId.Value;
        if (HostBeamSequences.TryGetValue(steamId, out var lastSequence) && sequence <= lastSequence)
            return false;

        HostBeamSequences[steamId] = sequence;
        return true;
    }

    private static bool TryGetHostState(Connection caller, GameObject target, out HostGrabState state)
    {
        state = null;
        if (caller is null)
            return false;

        if (!HostGrabStates.TryGetValue(caller.SteamId.Value, out state))
            return false;

        if (!state.IsValid())
        {
            HostRemoveJoint(state);
            if (state is not null)
                HostGrabStates.Remove(state.SteamId);
            return false;
        }

        if (target.IsValid() && state.GameObject != target)
            return false;

        return true;
    }

    private static bool HostCanUsePhysgun(Player player)
    {
        return player.IsValid()
            && player.IsAlive
            && !player.IsArrested
            && player.Controller.IsValid()
            && string.Equals(player.EquippedWeaponItemId, "physgun", StringComparison.OrdinalIgnoreCase);
    }

    private static Transform HostGetValidatedAimTransform(Player player, Vector3 requestedOrigin, Vector3 requestedForward, float requestedYaw)
    {
        var serverAim = player.Controller.IsValid()
            ? player.Controller.EyeTransform
            : new Transform(player.WorldPosition + Vector3.Up * 64f, player.WorldRotation);

        var origin = IsFiniteVector(requestedOrigin)
            && Vector3.DistanceBetween(serverAim.Position, requestedOrigin) <= HostMaxAimOriginError
                ? requestedOrigin
                : serverAim.Position;

        var forward = IsFiniteVector(requestedForward) && requestedForward.LengthSquared > 0.001f
            ? requestedForward.Normal
            : serverAim.Forward;

        if (forward.LengthSquared <= 0.001f && !float.IsNaN(requestedYaw) && !float.IsInfinity(requestedYaw))
            forward = Rotation.FromYaw(requestedYaw).Forward;

        if (forward.LengthSquared <= 0.001f)
            forward = Vector3.Forward;

        return new Transform(origin, Rotation.LookAt(forward.Normal));
    }

    private static Transform HostGetStateAimTransform(HostGrabState state)
    {
        var forward = IsFiniteVector(state.AimForward) && state.AimForward.LengthSquared > 0.001f
            ? state.AimForward.Normal
            : state.Player.WorldRotation.Forward;

        if (forward.LengthSquared <= 0.001f)
            forward = Vector3.Forward;

        return new Transform(state.AimPosition, Rotation.LookAt(forward.Normal));
    }

    private static bool IsFiniteVector(Vector3 value)
    {
        return !float.IsNaN(value.x) && !float.IsInfinity(value.x)
            && !float.IsNaN(value.y) && !float.IsInfinity(value.y)
            && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    }

    private static bool HostTryFindGrabTarget(Player player, GrabMode mode, Transform aim,
        float maxRange, float seekRadius, out SceneTraceResult tr, out Rigidbody rb)
    {
        tr = HostTraceGrabRay(player, aim.Position, aim.Forward, maxRange, 0f);
        if (HostTryGetGrabRigidbody(player, tr, mode, out rb))
            return true;

        if (tr.Hit || mode != GrabMode.Physgun || seekRadius <= 0f)
        {
            rb = null;
            return false;
        }

        tr = HostTraceGrabRay(player, aim.Position, aim.Forward, maxRange, seekRadius);
        return HostTryGetGrabRigidbody(player, tr, mode, out rb);
    }

    private static SceneTraceResult HostTraceGrabRay(Player player, Vector3 origin, Vector3 dir, float maxRange, float radius)
    {
        return player.Scene.Trace
            .Ray(origin, origin + dir * maxRange)
            .Radius(MathF.Max(0f, radius))
            .IgnoreGameObjectHierarchy(player.GameObject)
            .WithoutTags("bullet", "player")
            .Run();
    }

    private static bool HostTryGetGrabRigidbody(Player player, SceneTraceResult tr, GrabMode mode, out Rigidbody rb)
    {
        rb = null;

        if (!tr.Hit || !tr.GameObject.IsValid())
            return false;

        rb = tr.GameObject.Components.Get<Rigidbody>(FindMode.EverythingInSelfAndAncestors);
        if (!rb.IsValid() || !rb.GameObject.IsValid())
            return false;

        return HostCanGrabRigidbody(player, rb, mode);
    }

    private static bool HostCanGrabRigidbody(Player player, Rigidbody rb, GrabMode mode)
    {
        if (!player.IsValid() || !rb.IsValid() || !rb.GameObject.IsValid())
            return false;

        var prop = TryGetPropCustom(rb, out var propComponent);
        var shopObject = TryGetShopObject(rb, out var shopComponent);

        if (mode == GrabMode.GravityGun)
        {
            if (prop)
                return false;

            if (shopObject)
                return true;

            return HasGravityGunShopTarget(rb.GameObject);
        }

        if (shopObject && !prop)
            return false;

        if (prop)
            return propComponent.PlayerOwner.IsValid() && propComponent.PlayerOwner.CanTouchProp(propComponent, player);

        var printer = rb.GameObject.Components.Get<MoneyPrinterBase>(FindMode.EverythingInSelfAndAncestors);
        if (printer.IsValid() && IsSamePlayer(printer.PlayerOwner, player))
            return true;

        return false;
    }

    private static bool TryGetPropCustom(Rigidbody rb, out PropCustom prop)
    {
        prop = rb.GameObject.Components.Get<PropCustom>(FindMode.EverythingInSelfAndAncestors);
        return prop.IsValid();
    }

    private static bool TryGetShopObject(Rigidbody rb, out ShopObject shopObject)
    {
        shopObject = rb.GameObject.Components.Get<ShopObject>(FindMode.EverythingInSelfAndAncestors);
        return shopObject.IsValid();
    }

    private static bool HasGravityGunShopTarget(GameObject target)
    {
        if (target.Components.Get<ShopObject>(FindMode.EverythingInSelfAndAncestors).IsValid())
            return true;

        return target.Components.Get<MoneyPrinterBase>(FindMode.EverythingInSelfAndAncestors).IsValid()
            || target.Components.Get<Weed>(FindMode.EverythingInSelfAndAncestors).IsValid()
            || target.Components.Get<WeedFertilizer>(FindMode.EverythingInSelfAndAncestors).IsValid();
    }

    private static bool IsShopGravityGunTarget(Rigidbody rb)
    {
        return TryGetShopObject(rb, out _) || HasGravityGunShopTarget(rb.GameObject);
    }

    private static bool HostIsWithinShopGravityGunDistance(Player player, Rigidbody rb)
    {
        if (!player.IsValid() || !rb.IsValid())
            return false;

        return Vector3.DistanceBetween(player.WorldPosition, rb.WorldPosition) <= ShopGravityGunMaxDistance;
    }

    private static bool IsHeldByAnotherPlayer(GameObject target, long grabberSteamId)
    {
        if (!target.IsValid())
            return false;

        if (!HostHeldObjectGrabbers.TryGetValue(target, out var holderSteamId))
            return false;

        return holderSteamId != grabberSteamId;
    }

    private static void HostRegisterHeldObject(GameObject target, long grabberSteamId)
    {
        if (!target.IsValid() || grabberSteamId == 0L)
            return;

        HostHeldObjectGrabbers[target] = grabberSteamId;
    }

    private static void HostUnregisterHeldObject(GameObject target)
    {
        if (!target.IsValid())
            return;

        HostHeldObjectGrabbers.Remove(target);
    }

    private static void HostReleaseNetworkOwnership(GameObject target, Connection caller)
    {
        if (!Networking.IsHost || !target.IsValid() || caller is null)
            return;

        HostUnregisterHeldObject(target);

        if (target.Network.Owner == caller)
            target.Network.DropOwnership();
    }

    private static bool HostCanHostSimulateGrabbedBody(HostGrabState state)
    {
        return state.Body.IsValid() && !state.Body.IsProxy;
    }

    private static bool HostCanMoveBody(Rigidbody body)
    {
        return body.IsValid()
            && !body.IsProxy
            && body.MotionEnabled
            && body.PhysicsBody.IsValid();
    }

    private static Transform GetTraceBodyTransform(SceneTraceResult tr, Rigidbody rb)
    {
        if (tr.Body is not null)
            return tr.Body.Transform.WithScale(rb.GameObject.WorldScale);

        return rb.WorldTransform.WithScale(rb.GameObject.WorldScale);
    }

    private static Vector3 GetPullLocalOffset(Rigidbody rb, Transform bodyTransform)
    {
        if (!rb.IsValid() || !rb.PhysicsBody.IsValid())
            return Vector3.Zero;

        var bodyScale = new Transform(Vector3.Zero, Rotation.Identity, bodyTransform.Scale);
        return bodyScale.PointToLocal(rb.PhysicsBody.LocalMassCenter);
    }

    private static void HostRemoveJoint(HostGrabState state)
    {
        if (state is null)
            return;

        state.Joint?.Remove();
        state.Joint = null;

        state.ControlBody?.Remove();
        state.ControlBody = null;
    }

    private static void HostApplyGrabMovement(HostGrabState state)
    {
        var body = state.Body;
        if (!HostCanMoveBody(body) || !state.Player.IsValid())
        {
            HostRemoveJoint(state);
            return;
        }

        var eye = HostGetStateAimTransform(state);
        var grabDistance = HostClampGrabDistance(body, HostGetEndPoint(state), eye,
            state.GrabDistance, state.MinHoldDistance, state.MaxHoldDistance);
        var targetPos = eye.Position + eye.Forward * grabDistance;

        var targetRot = state.Mode == GrabMode.GravityGun
            ? eye.Rotation * state.GrabOffset
            : Rotation.FromYaw(state.AimYaw) * state.GrabOffset;

        var desiredBodyPos = targetPos - targetRot * state.LocalOffset;
        desiredBodyPos = HostResolveHeldPlayerOverlaps(state, desiredBodyPos,
            state.PlayerPadding, state.MaxResolveIterations);
        targetPos = desiredBodyPos + targetRot * state.LocalOffset;

        state.ControlBody ??= new PhysicsBody(state.Player.Scene.PhysicsWorld)
        {
            BodyType = PhysicsBodyType.Keyframed,
            AutoSleep = false
        };

        state.ControlBody.Transform = new Transform(targetPos, targetRot);

        if (state.Joint is not null)
            return;

        var physicsBody = body.PhysicsBody;
        var bodyTransform = body.WorldTransform.WithScale(1f);
        var point1 = new PhysicsPoint(state.ControlBody);
        var point2 = new PhysicsPoint(physicsBody, bodyTransform.PointToLocal(HostGetEndPoint(state)));
        var maxForce = physicsBody.Mass * physicsBody.World.Gravity.LengthSquared;

        state.Joint = PhysicsJoint.CreateControl(point1, point2);
        state.Joint.LinearSpring = new PhysicsSpring(32f, 4f, maxForce);
        state.Joint.AngularSpring = new PhysicsSpring(64f, 4f, maxForce * 3f);
    }

    private static void HostUpdateSyncedBeam(HostGrabState state)
    {
        if (!state.Player.IsValid() || !state.Body.IsValid())
            return;

        if (state.Mode == GrabMode.GravityGun)
        {
            state.BeamHasLastEnd = false;
            state.BeamBend = Vector3.Zero;
            state.Player.SetPhysgunBeam(false);
            return;
        }

        var start = state.Player.GetPhysgunBeamWorldStart(state.AimForward);
        var end = state.Body.WorldTransform.PointToWorld(state.LocalOffset);
        var endNormal = HostGetEndNormal(state);

        end = HostClampBeamEnd(start, end);

        var dt = MathF.Max(Time.Delta, 0.001f);
        var endVelocity = state.BeamHasLastEnd ? (end - state.BeamLastEnd) / dt : Vector3.Zero;
        state.BeamLastEnd = end;
        state.BeamHasLastEnd = true;

        var distance = Vector3.DistanceBetween(start, end);
        var sag = Vector3.Down * MathF.Min(HostSyncedBeamSag, distance * 0.08f);
        var moveBend = -endVelocity * HostSyncedBeamMoveBendScale;
        var desiredBend = ClampVectorLength(sag + moveBend, HostSyncedBeamMaxBend);

        state.BeamBend = LerpVector(state.BeamBend, desiredBend, 0.35f);
        state.Player.SetPhysgunBeam(true, start, end, state.BeamBend, endNormal, grabbed: true);
    }

    private static Vector3 HostClampBeamEnd(Vector3 start, Vector3 end)
    {
        var delta = end - start;
        if (delta.Length <= HostMaxRangeLimit)
            return end;

        return start + delta.Normal * HostMaxRangeLimit;
    }

    private static Vector3 HostValidateBeamStart(Player player, Vector3 requestedStart, Vector3 requestedEnd)
    {
        var fallbackForward = IsFiniteVector(requestedEnd - requestedStart) && (requestedEnd - requestedStart).LengthSquared > 0.001f
            ? (requestedEnd - requestedStart).Normal
            : player.WorldRotation.Forward;

        var fallbackStart = player.GetPhysgunBeamWorldStart(fallbackForward);

        if (!IsFiniteVector(requestedStart))
            return fallbackStart;

        var serverEye = player.Controller.IsValid()
            ? player.Controller.EyeTransform.Position
            : player.WorldPosition + Vector3.Up * 64f;

        if (Vector3.DistanceBetween(serverEye, requestedStart) > HostMaxAimOriginError)
            return fallbackStart;

        return requestedStart;
    }

    private static Vector3 HostValidateBeamEnd(Vector3 start, Vector3 requestedEnd, float requestedMaxRange)
    {
        if (!IsFiniteVector(requestedEnd))
            return start;

        var maxRange = Clamp(FiniteOrDefault(requestedMaxRange, 8196f), 64f, HostMaxRangeLimit);
        var delta = requestedEnd - start;
        if (delta.Length <= maxRange)
            return requestedEnd;

        return start + delta.Normal * maxRange;
    }

    private static float HostClampGrabDistance(Rigidbody body, Vector3 point, Transform eye,
        float distance, float min, float max)
    {
        distance = Clamp(distance, min, max);

        if (!body.IsValid())
            return distance;

        var closest = body.FindClosestPoint(eye.Position);
        var along = distance + Vector3.Dot(closest - point, eye.Rotation.Forward);
        if (along < min)
            distance += min - along;

        return Clamp(distance, min, max);
    }

    private static Vector3 HostGetEndPoint(HostGrabState state)
    {
        if (!state.GameObject.IsValid())
            return state.LocalOffset;

        return state.GameObject.WorldTransform.PointToWorld(state.LocalOffset);
    }

    private static Vector3 HostGetEndNormal(HostGrabState state)
    {
        if (!state.GameObject.IsValid())
            return state.AimForward.LengthSquared > 0.001f ? -state.AimForward.Normal : Vector3.Up;

        var normal = state.GameObject.WorldTransform.NormalToWorld(state.LocalNormal);
        return normal.LengthSquared > 0.001f ? normal.Normal : Vector3.Up;
    }

    private static void HostResolveGrabbedPlayerOverlaps(HostGrabState state, bool preserveVelocity,
        float padding, float pushSpeed, int maxIterations)
    {
        var rb = state.Body;
        if (!rb.IsValid() || !rb.GameObject.IsValid()) return;

        var moved = false;
        for (var i = 0; i < maxIterations; i++)
        {
            if (!HostTryFindPlayerReleaseOffset(state, padding, out var offset))
                break;

            rb.WorldPosition += offset;
            moved = true;
        }

        if (!moved)
            return;

        if (!preserveVelocity)
        {
            rb.Velocity = Vector3.Zero;
            rb.AngularVelocity = Vector3.Zero;
            return;
        }

        if (rb.Velocity.LengthSquared <= 1f)
            rb.Velocity = HostGetSafeReleaseDirection(state) * pushSpeed;
    }

    private static Vector3 HostResolveHeldPlayerOverlaps(HostGrabState state, Vector3 desiredBodyPos,
        float padding, int maxIterations)
    {
        var rb = state.Body;
        if (!rb.IsValid())
            return desiredBodyPos;

        var currentBounds = HostGetRigidBodyBounds(rb);
        var resolvedOffset = desiredBodyPos - rb.WorldPosition;

        for (var i = 0; i < maxIterations; i++)
        {
            var desiredBounds = currentBounds.Translate(resolvedOffset);
            if (!HostTryFindPlayerOverlapOffset(state, desiredBounds, padding, out var pushOffset))
                break;

            resolvedOffset += pushOffset;
        }

        return rb.WorldPosition + resolvedOffset;
    }

    private static bool HostTryFindPlayerReleaseOffset(HostGrabState state, float padding, out Vector3 offset)
    {
        offset = Vector3.Zero;
        var propBounds = HostGetRigidBodyBounds(state.Body);

        foreach (var go in state.Player.Scene.GetAllObjects(true))
        {
            if (!go.Components.TryGet<Player>(out var player))
                continue;

            if (!player.IsValid() || !player.Controller.IsValid())
                continue;

            var playerBounds = player.Controller.BodyBox().Grow(padding);
            if (!propBounds.Overlaps(playerBounds))
                continue;

            offset = HostGetHorizontalSeparationOffset(propBounds, playerBounds, padding);
            if (offset.LengthSquared <= 0.001f)
                offset = HostGetFallbackReleaseDirection(state, player) * MathF.Max(16f, padding + 8f);

            return true;
        }

        return false;
    }

    private static bool HostTryFindPlayerOverlapOffset(HostGrabState state, BBox propBounds,
        float padding, out Vector3 offset)
    {
        offset = Vector3.Zero;

        foreach (var go in state.Player.Scene.GetAllObjects(true))
        {
            if (!go.Components.TryGet<Player>(out var player))
                continue;

            if (!player.IsValid() || !player.Controller.IsValid())
                continue;

            var playerBounds = player.Controller.BodyBox().Grow(padding);
            if (!propBounds.Overlaps(playerBounds))
                continue;

            offset = HostGetHorizontalSeparationOffset(propBounds, playerBounds, padding);
            if (offset.LengthSquared <= 0.001f)
                offset = HostGetFallbackReleaseDirection(state, player) * MathF.Max(16f, padding + 8f);

            return true;
        }

        return false;
    }

    private static BBox HostGetRigidBodyBounds(Rigidbody rb)
    {
        if (rb.PhysicsBody is not null)
            return rb.PhysicsBody.GetBounds();

        return rb.GetWorldBounds();
    }

    private static Vector3 HostGetHorizontalSeparationOffset(BBox propBounds, BBox playerBounds, float padding)
    {
        padding = MathF.Max(0f, padding);

        var moveRight = playerBounds.Maxs.x - propBounds.Mins.x + padding;
        var moveLeft = playerBounds.Mins.x - propBounds.Maxs.x - padding;
        var moveForward = playerBounds.Maxs.y - propBounds.Mins.y + padding;
        var moveBack = playerBounds.Mins.y - propBounds.Maxs.y - padding;

        var moveX = MathF.Abs(moveRight) < MathF.Abs(moveLeft) ? moveRight : moveLeft;
        var moveY = MathF.Abs(moveForward) < MathF.Abs(moveBack) ? moveForward : moveBack;

        return MathF.Abs(moveX) < MathF.Abs(moveY)
            ? new Vector3(moveX, 0f, 0f)
            : new Vector3(0f, moveY, 0f);
    }

    private static Vector3 HostGetFallbackReleaseDirection(HostGrabState state, Player player)
    {
        var away = state.Body.WorldPosition - player.WorldPosition;
        away.z = 0f;

        if (away.LengthSquared > 0.001f)
            return away.Normal;

        if (state.Player.IsValid() && state.Player.Controller.IsValid())
        {
            var forward = state.Player.Controller.EyeTransform.Forward;
            forward.z = 0f;
            if (forward.LengthSquared > 0.001f)
                return forward.Normal;
        }

        return Vector3.Right;
    }

    private static Vector3 HostGetSafeReleaseDirection(HostGrabState state)
    {
        var away = state.Body.WorldPosition - state.Player.WorldPosition;
        away.z = 0f;
        return away.LengthSquared > 0.001f ? away.Normal : Vector3.Right;
    }

    private static void HostDisableHeldCollisionMode(HostGrabState state)
    {
        if (state.Body.IsValid() && state.Body.GameObject.IsValid() && !state.HadHeldCollisionTag)
            state.Body.GameObject.Tags.Remove(HeldCollisionTag);
    }

    private static void HostRejectGrab(Connection caller, GameObject target)
    {
        if (caller is null)
            return;

        if (TryGetCallerPlayer(caller, out var player))
            player.SetPhysgunBeam(false);

        using (Rpc.FilterInclude(c => c.SteamId.Value == caller.SteamId.Value))
        {
            RpcClientRejectGrab(caller.SteamId.Value, target);
        }
    }

    [Rpc.Broadcast]
    private static void RpcClientRejectGrab(long steamId, GameObject target)
    {
        if (Connection.Local?.SteamId.Value != steamId)
            return;

        if (Player.Local?.CurrentWeapon is WeaponPhysgun physgun)
            physgun.OnHostRejectedGrab(target);
    }

    private void OnHostRejectedGrab(GameObject target)
    {
        if (target.IsValid() && _grabbed.IsValid() && _grabbed.GameObject != target)
            return;

        ResetGrab();
        _preventReselect = true;
    }

    [Rpc.Broadcast]
    private static void RpcBroadcastPhysgunSound(SoundEvent sound, Vector3 position)
    {
        if (sound.IsValid())
            Sound.Play(sound, position);
    }

    private static void HostBroadcastPhysgunEffect(GameObject effectPrefab, GameObject target, Transform transform)
    {
        if (!effectPrefab.IsValid())
            return;

        RpcBroadcastPhysgunEffect(effectPrefab, target, transform.Position, transform.Rotation);
    }

    [Rpc.Broadcast]
    private static void RpcBroadcastPhysgunEffect(GameObject effectPrefab, GameObject target, Vector3 position, Rotation rotation)
    {
        if (!effectPrefab.IsValid())
            return;

        var effect = effectPrefab.Clone(new Transform(position, rotation));
        if (!effect.IsValid())
            return;

        foreach (var emitter in effect.GetComponentsInChildren<ParticleModelEmitter>())
            emitter.Target = target;
    }

    private void UpdateSoundState()
    {
        var position = GetLocalSoundPosition();
        EnsureIdleSound(position);
        UpdateIdleSoundPosition(position);
    }

    private void EnsureIdleSound(Vector3 position)
    {
        if (_idleSoundHandle is not null)
            return;

        var sound = ResolveSound(IdleSound, DefaultIdleSound);
        if (!sound.IsValid())
            return;

        _idleSoundHandle = Sound.Play(sound, position);
    }

    private void StopPhysgunSounds()
    {
        _idleSoundHandle?.Stop();
        _idleSoundHandle = null;
    }

    private void UpdateIdleSoundPosition(Vector3 position)
    {
        if (_idleSoundHandle is not null)
            _idleSoundHandle.Position = position;
    }

    private Vector3 GetLocalSoundPosition()
    {
        return ShotPos.IsValid() ? ShotPos.WorldPosition : WorldPosition;
    }

    private void PlayLocalSound(SoundEvent sound)
    {
        PlayLocalSound(sound, null);
    }

    private void PlayLocalSound(SoundEvent sound, SoundEvent fallback)
    {
        sound = ResolveSound(sound, fallback);
        if (!sound.IsValid()) return;

        Sound.Play(sound, GetLocalSoundPosition());
    }

    private static SoundEvent ResolveSound(SoundEvent sound, SoundEvent fallback)
    {
        return sound.IsValid() ? sound : fallback;
    }

    private void UpdateBeamState()
    {
        if (_mode == GrabMode.PhysgunSeek && _beamSeekActive)
        {
            UpdateSeekingBeamState();
            return;
        }

        if (_mode == GrabMode.None || !_grabbed.IsValid())
        {
            ResetBeamState();
            return;
        }

        if (_mode == GrabMode.GravityGun)
        {
            ResetBeamState();
            return;
        }

        var player = Player.Local;
        if (!player.IsValid() || !player.Controller.IsValid())
        {
            ResetBeamState();
            return;
        }

        var eye = player.Controller.EyeTransform;
        var start = ShotPos.IsValid() ? ShotPos.WorldPosition : eye.Position;
        var end = GetBeamEndPosition();
        var endNormal = GetBeamEndNormal(eye.Forward);
        end = ClampBeamEnd(start, end);

        var dt = MathF.Max(Time.Delta, 0.001f);
        var endVelocity = _beamHasLastEnd ? (end - _beamLastEnd) / dt : Vector3.Zero;
        _beamLastEnd = end;
        _beamHasLastEnd = true;

        var distance = Vector3.DistanceBetween(start, end);
        var sag = Vector3.Down * MathF.Min(BeamSag, distance * 0.08f);
        var moveBend = -endVelocity * BeamMoveBendScale;
        var desiredBend = ClampVectorLength(sag + moveBend, BeamMaxBend);

        _beamBend = LerpVector(_beamBend, desiredBend, 0.35f);
        PublishBeamState(true, start, end, _beamBend, endNormal, grabbed: true);
    }

    private void UpdateSeekingBeamState()
    {
        var player = Player.Local;
        if (!player.IsValid() || !player.Controller.IsValid())
        {
            ResetBeamState();
            return;
        }

        var eye = player.Controller.EyeTransform;
        var start = ShotPos.IsValid() ? ShotPos.WorldPosition : eye.Position;
        var end = eye.Position + eye.Forward * MathF.Max(1f, MathF.Min(MaxRange, BeamMaxLength));
        var endNormal = -eye.Forward;

        var visualTrace = Scene.Trace
            .Ray(eye.Position, eye.Position + eye.Forward * MaxRange)
            .IgnoreGameObjectHierarchy(player.GameObject)
            .WithoutTags("bullet", "player")
            .Run();

        if (visualTrace.Hit)
        {
            end = ClampBeamEnd(start, visualTrace.HitPosition);
            endNormal = visualTrace.Normal;
        }

        var desiredBend = GetSeekingBeamBend(eye, start, end);
        _beamBend = LerpVector(_beamBend, desiredBend, 0.25f);
        _beamLastEnd = end;
        _beamHasLastEnd = true;
        PublishBeamState(true, start, end, _beamBend, endNormal, grabbed: false);
    }

    private Vector3 GetSeekingBeamBend(Transform eye, Vector3 start, Vector3 end)
    {
        var delta = end - start;
        var dir = delta.LengthSquared > 0.001f ? delta.Normal : eye.Forward;
        var side = CrossVector(dir, Vector3.Up);
        if (side.LengthSquared <= 0.001f)
            side = CrossVector(dir, Vector3.Right);

        side = side.LengthSquared > 0.001f ? side.Normal : Vector3.Right;
        var up = CrossVector(side, dir);
        up = up.LengthSquared > 0.001f ? up.Normal : Vector3.Up;

        var t = Time.Now * MathF.Max(0.1f, BeamSeekNoiseSpeed);
        var lengthFactor = MathF.Min(1f, delta.Length / MathF.Max(1f, BeamMaxLength));
        var pulse = 0.75f + MathF.Sin(t * 1.7f) * 0.25f;
        var idleBend = MathF.Max(0f, BeamSeekIdleBend) * lengthFactor * pulse;

        return (side * MathF.Sin(t * 1.31f) + up * MathF.Cos(t * 0.83f)) * idleBend;
    }

    private Vector3 GetBeamEndPosition()
    {
        if (!_grabbed.IsValid())
            return Vector3.Zero;

        return _grabbed.WorldTransform.PointToWorld(_localOffset);
    }

    private Vector3 GetBeamEndNormal(Vector3 fallbackForward)
    {
        if (!_grabbed.IsValid())
            return fallbackForward.LengthSquared > 0.001f ? -fallbackForward.Normal : Vector3.Up;

        if (_mode == GrabMode.GravityGun)
            return fallbackForward.LengthSquared > 0.001f ? -fallbackForward.Normal : Vector3.Up;

        var normal = _grabbed.WorldTransform.NormalToWorld(_localNormal);
        return normal.LengthSquared > 0.001f ? normal.Normal : Vector3.Up;
    }

    private Vector3 ClampBeamEnd(Vector3 start, Vector3 end)
    {
        var maxLength = MathF.Max(1f, MathF.Min(MaxRange, BeamMaxLength));
        var delta = end - start;
        if (delta.Length <= maxLength)
            return end;

        return start + delta.Normal * maxLength;
    }

    private void ResetBeamState()
    {
        var hadBeam = _beamHasLastEnd;
        _beamLastEnd = Vector3.Zero;
        _beamBend = Vector3.Zero;
        _beamHasLastEnd = false;

        if (Player.Local.IsValid() && (hadBeam || Player.Local.PhysgunBeamActive))
            PublishBeamState(false);
    }

    private void PublishBeamState(bool active, Vector3 start = default, Vector3 end = default, Vector3 bend = default,
        Vector3 endNormal = default, bool grabbed = false)
    {
        if (!Player.Local.IsValid())
            return;

        Player.Local.SetLocalPhysgunBeam(active, start, end, bend, endNormal, grabbed);

        if (Networking.IsHost)
        {
            Player.Local.SetPhysgunBeam(active, start, end, bend, endNormal, grabbed);
            return;
        }

        var sequence = unchecked(++_beamInputSequence);

        if (active)
            RpcHostUpdatePhysgunBeam(sequence, start, end, bend, endNormal, grabbed, MaxRange);
        else
            RpcHostClearPhysgunBeam(sequence);
    }

    private void UpdateViewmodelState()
    {
        if (Viewmodel == null)
            return;

        var player = Player.Local;
        var controller = player?.Controller;
        var active = _mode == GrabMode.PhysgunSeek || _mode == GrabMode.Physgun || _mode == GrabMode.GravityGun;
        var hovered = _mode == GrabMode.PhysgunSeek || _pullHoverActive || HasLocalHoverTarget();
        var stylus = 0f;
        var brake = 0f;

        if (hovered)
            stylus = 0.5f;

        if (active)
        {
            stylus = 1f;
            _attackHold = Clamp(_attackHold + Time.Delta, 0f, 1f);
        }
        else
        {
            _attackHold = 0f;
        }

        if (active || _pullHoverActive)
            brake = 1f;

        if (controller.IsValid())
        {
            var eyeAngles = controller.EyeAngles;
            if (!_viewmodelInertiaInitialized)
            {
                _lastViewmodelEyeAngles = eyeAngles;
                _viewmodelInertia = Vector2.Zero;
                _viewmodelInertiaInitialized = true;
            }
            else
            {
                _viewmodelInertia = new Vector2(
                    Angles.NormalizeAngle(eyeAngles.pitch - _lastViewmodelEyeAngles.pitch),
                    Angles.NormalizeAngle(_lastViewmodelEyeAngles.yaw - eyeAngles.yaw));
                _lastViewmodelEyeAngles = eyeAngles;
            }

            var velocity = controller.Body.Velocity;
            var eye = controller.EyeTransform;
            var forward = Vector3.Dot(eye.Forward, velocity);
            var sideward = Vector3.Dot(eye.Right, velocity);
            var horizontalSpeed = new Vector3(velocity.x, velocity.y, 0f).Length;
            var direction = MathF.Atan2(sideward, forward).RadianToDegree().NormalizeDegrees();
            var grounded = controller.IsOnGround;

            if (_wasGrounded && !grounded)
                Viewmodel.Set("b_jump", true);

            _wasGrounded = grounded;

            Viewmodel.Set("b_grounded", grounded);
            Viewmodel.Set("aim_pitch", eyeAngles.pitch);
            Viewmodel.Set("aim_yaw", eyeAngles.yaw);
            Viewmodel.Set("aim_pitch_inertia", _viewmodelInertia.x * 2f);
            Viewmodel.Set("aim_yaw_inertia", _viewmodelInertia.y * 2f);
            Viewmodel.Set("move_direction", direction);
            Viewmodel.Set("move_speed", velocity.Length);
            Viewmodel.Set("move_groundspeed", horizontalSpeed);
            Viewmodel.Set("move_x", forward);
            Viewmodel.Set("move_y", sideward);
            Viewmodel.Set("move_z", velocity.z);
            Viewmodel.Set("move_bob", Clamp(horizontalSpeed / MathF.Max(1f, controller.RunSpeed * 2f), 0f, 1f));
        }
        else
        {
            _viewmodelInertiaInitialized = false;
            _wasGrounded = true;
        }

        Viewmodel.Set("b_twohanded", true);
        Viewmodel.Set("stylus", stylus);
        Viewmodel.Set("brake", brake);
        Viewmodel.Set("b_button", _spinCameraLocked);
        Viewmodel.Set("b_attack", _mode == GrabMode.Physgun || _mode == GrabMode.PhysgunSeek);
        Viewmodel.Set("attack_hold", _attackHold);
        Viewmodel.Set("ironsights", 0);
        Viewmodel.Set("ironsights_fire_scale", 0.5f);
        Viewmodel.Set("speed_ironsights", 1f);
        Viewmodel.Set("deploy_type", 0);
        Viewmodel.Set("reload_type", 0);
        Viewmodel.Set("speed_deploy", 1f);
        Viewmodel.Set("speed_reload", 1f);
        Viewmodel.Set("speed_grab", 1f);
        Viewmodel.Set("b_holster", false);
        Viewmodel.Set("b_lower_weapon", false);
        Viewmodel.Set("b_empty", false);
        Viewmodel.Set("firing_mode", 0);

        if (active)
            Viewmodel.Set("b_sprint", false);
    }

    private bool HasLocalHoverTarget()
    {
        if (!Player.Local.IsValid() || !Player.Local.Controller.IsValid())
            return false;

        var eye = Player.Local.Controller.EyeTransform;
        return TryFindGrabTarget(GrabMode.Physgun, eye.Position, eye.Forward, out _, out _)
            || TryFindGrabTarget(GrabMode.GravityGun, eye.Position, eye.Forward, out _, out _);
    }

    private static Vector3 ClampVectorLength(Vector3 value, float maxLength)
    {
        maxLength = MathF.Max(0f, maxLength);
        if (maxLength <= 0f || value.Length <= maxLength)
            return value;

        return value.Normal * maxLength;
    }

    private static Vector3 LerpVector(Vector3 a, Vector3 b, float t)
    {
        return a + (b - a) * t;
    }

    private static Vector3 CrossVector(Vector3 a, Vector3 b)
    {
        return new Vector3(
            a.y * b.z - a.z * b.y,
            a.z * b.x - a.x * b.z,
            a.x * b.y - a.y * b.x
        );
    }

    private static bool IsSamePlayer(Player owner, Player player)
    {
        if (!owner.IsValid() || !player.IsValid())
            return false;

        if (owner == player)
            return true;

        var ownerSteamId = owner.GameObject.Network.Owner?.SteamId.Value ?? 0L;
        var playerSteamId = player.GameObject.Network.Owner?.SteamId.Value ?? 0L;
        return ownerSteamId != 0L && ownerSteamId == playerSteamId;
    }

    private static void GetConnectedBodies(GameObject source, HashSet<Rigidbody> result)
    {
        if (!source.IsValid())
            return;

        foreach (var rb in source.Root.Components.GetAll<Rigidbody>())
        {
            if (!rb.IsValid() || !result.Add(rb))
                continue;

            foreach (var joint in rb.Joints)
            {
                if (joint.Object1 != null)
                    GetConnectedBodies(joint.Object1, result);

                if (joint.Object2 != null)
                    GetConnectedBodies(joint.Object2, result);
            }
        }
    }

    private static float Clamp(float value, float min, float max)
    {
        if (max < min)
            max = min;

        return MathF.Max(min, MathF.Min(max, value));
    }

    private static bool Nearly(float a, float b, float epsilon = 0.001f)
    {
        return MathF.Abs(a - b) <= epsilon;
    }

    private static float FiniteOrDefault(float value, float fallback)
    {
        return float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;
    }
}
