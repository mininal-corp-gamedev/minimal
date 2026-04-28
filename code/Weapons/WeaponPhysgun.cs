using Sandbox;
using System;

namespace Minimal.Weapons;

/// <summary>
/// Physgun + GravityGun в одном стволе (без эффектов и лучей).
///
/// Управление:
/// * Зажатие <b>ЛКМ</b> — physgun: держит объект на текущей дистанции перед игроком.
///   <list type="bullet">
///   <item>Колёсико мыши — приближать/отдалять объект.</item>
///   <item>Зажать <b>Reload</b> — крутить объект (<see cref="Input.AnalogLook"/>).</item>
///   <item>Зажать <b>Run</b> вместе с Reload — крутить со снэпом по углу <see cref="SnapAngleDegrees"/>.</item>
///   <item>Нажать <b>ПКМ</b> — заморозить объект (отключить физику) и отпустить захват.</item>
///   </list>
/// * Зажатие <b>ПКМ</b> — gravitygun: подтягивает объект к игроку и удерживает близко.
///   <list type="bullet">
///   <item>Нажать <b>ЛКМ</b> — швырнуть объект вперёд (push).</item>
///   <item>Отпустить <b>ПКМ</b> — просто бросить (без push).</item>
///   </list>
///
/// Захват разрешён только для GameObject, у которого есть <see cref="Rigidbody"/>
/// и которым владеет локальный игрок (<c>GameObject.Network.Owner == Connection.Local</c>).
/// Этим мы соблюдаем server (host) authority: хост по сети авторитетен глобально,
/// но физикой конкретного объекта рулит его сетевой владелец — поэтому именно он
/// и манипулирует им через physgun.
/// </summary>
public sealed class WeaponPhysgun : Weapon
{
    private const string HeldCollisionTag = "physgun_held";

    [Property, Category("Physgun")] public float MaxRange { get; set; } = 1024f;
    [Property, Category("Physgun")] public float MinHoldDistance { get; set; } = 60f;
    [Property, Category("Physgun")] public float MaxHoldDistance { get; set; } = 1024f;
    [Property, Category("Physgun")] public float GravityGunHoldDistance { get; set; } = 90f;
    [Property, Category("Physgun")] public float ScrollStep { get; set; } = 25f;
    [Property, Category("Physgun")] public float MaxLinearSpeed { get; set; } = 4000f;
    [Property, Category("Physgun")] public float LaunchForce { get; set; } = 1500f;
    [Property, Category("Physgun")] public float SnapAngleDegrees { get; set; } = 45f;
    [Property, Category("Physgun")] public float RotationLerp { get; set; } = 0.5f;
    [Property, Category("Physgun")] public float SeekRadius { get; set; } = 28f;
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

    // Physgun не использует систему патронов и перезарядку.
    protected override bool UseDefaultCombatInput => false;

    private enum GrabMode
    {
        None,
        /// <summary>ЛКМ зажат, луч активен, ждём первый валидный объект под прицелом.</summary>
        PhysgunSeek,
        /// <summary>ЛКМ — обычный physgun: держим на расстоянии _grabDistance.</summary>
        Physgun,
        /// <summary>ПКМ — gravitygun: подтягиваем близко и держим.</summary>
        GravityGun
    }

    private GrabMode _mode = GrabMode.None;
    private Rigidbody _grabbed;

    /// <summary>Дистанция вдоль взгляда, где должна находиться точка захвата на объекте.</summary>
    private float _grabDistance;

    /// <summary>
    /// Локальная точка на объекте (в его собственной системе координат), которую мы стараемся
    /// удерживать в позиции <c>eyeOrigin + eyeForward * _grabDistance</c>.
    /// </summary>
    private Vector3 _localOffset;

    /// <summary>
    /// Желаемый поворот объекта относительно опорного фрейма (yaw игрока для physgun,
    /// полный взгляд для gravitygun). Меняется при крутке Reload.
    /// </summary>
    private Rotation _grabOffset = Rotation.Identity;

    /// <summary>
    /// True если только что отпустили захват: блокирует немедленную пере-захват той же кнопкой,
    /// пока ЛКМ/ПКМ не будут отпущены.
    /// </summary>
    private bool _preventReselect;

    /// <summary>
    /// Угол камеры, который мы фиксируем на время крутки объекта (Reload в physgun).
    /// Захватывается на кадре нажатия Reload и каждый следующий кадр восстанавливается,
    /// чтобы движение мыши крутило объект, а не камеру.
    /// </summary>
    private Angles _spinSavedEyeAngles;
    private bool _spinCameraLocked;
    private bool _spinLookControlsOverridden;
    private bool _spinPreviousUseLookControls;
    private Vector3 _beamLastEnd;
    private Vector3 _beamBend;
    private bool _beamHasLastEnd;
    private bool _grabbedHadHeldCollisionTag;
    private bool _beamSeekActive;

    protected override void OnWeaponStart()
    {
        HoldType = WeaponHoldType.PhysGun;
        Ammo = 0;
        Log.Info("[WeaponPhysgun] Ready");
    }

    protected override void OnDisabled()
    {
        ResetGrab(resolvePlayerOverlap: true);
    }

    protected override void OnWeaponFixedUpdate()
    {
        if (!Player.Local.IsValid()) return;
        if (Player.Local.IsArrested) { ResetGrab(resolvePlayerOverlap: true); return; }
        if (!Player.Local.Controller.IsValid()) return;

        ValidateGrabbed();
        HandleInput();
        UpdateGrabbed();
        UpdateBeamState();
    }

    protected override void OnWeaponUpdate()
    {
        // Крутка объекта работает по «живой» дельте мыши, поэтому делаем её
        // на каждом кадре, а не в FixedUpdate (где Input.AnalogLook повторяется
        // между двумя физическими шагами одного кадра).
        UpdateSpin();
    }

    private void ValidateGrabbed()
    {
        if (_mode == GrabMode.None || _mode == GrabMode.PhysgunSeek) return;

        if (!_grabbed.IsValid() || !_grabbed.GameObject.IsValid())
        {
            ResetGrab();
            return;
        }

        // Если объект внезапно сменил сетевого владельца — отпускаем.
        if (_grabbed.GameObject.Network.Owner != Connection.Local)
        {
            ResetGrab();
        }
    }

    // ============================ ВВОД ============================

    private void HandleInput()
    {
        bool lmbDown = Input.Down("Attack1");
        bool rmbDown = Input.Down("Attack2");
        bool lmbPressed = Input.Pressed("Attack1");
        bool rmbPressed = Input.Pressed("Attack2");

        // После любого отпускания захвата ждём, пока пользователь отпустит обе кнопки,
        // чтобы он не схватил ту же штуку ещё раз тем же кликом.
        if (_preventReselect)
        {
            if (!lmbDown && !rmbDown) _preventReselect = false;
            return;
        }

        if (_mode == GrabMode.None)
        {
            // Приоритет у ПКМ: если игрок нажал обе сразу — это явно gravitygun.
            if (rmbPressed)
            {
                TryStartGrab(GrabMode.GravityGun);
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
            HandlePhysgunSeekInput(lmbDown);
            return;
        }

        if (_mode == GrabMode.Physgun)
        {
            HandlePhysgunInput(lmbDown, rmbPressed);
            return;
        }

        if (_mode == GrabMode.GravityGun)
        {
            HandleGravityGunInput(rmbDown, lmbPressed);
            return;
        }
    }

    private void HandlePhysgunInput(bool lmbDown, bool rmbPressed)
    {
        // ПКМ во время удержания ЛКМ — заморозить объект и отпустить.
        if (rmbPressed)
        {
            FreezeGrabbed();
            EndGrab();
            return;
        }

        // Отпустили ЛКМ — просто отпустили объект.
        if (!lmbDown)
        {
            EndGrab();
            return;
        }

        // Колёсико мыши — менять дистанцию удержания.
        var wheelY = Input.MouseWheel.y;
        if (MathF.Abs(wheelY) > 0.001f)
        {
            _grabDistance = MathF.Max(MinHoldDistance, MathF.Min(MaxHoldDistance, _grabDistance + wheelY * ScrollStep));
            // Глушим колёсико, чтобы не переключился слот в инвентаре.
            Input.MouseWheel = Vector2.Zero;
        }

        // Сама крутка по AnalogLook делается в OnWeaponUpdate (UpdateSpin) — там
        // живая дельта мыши и можно корректно зафиксировать камеру.
    }

    private void HandlePhysgunSeekInput(bool lmbDown)
    {
        if (!lmbDown)
        {
            EndSeek();
            return;
        }

        TryStartGrab(GrabMode.Physgun);
    }

    private void HandleGravityGunInput(bool rmbDown, bool lmbPressed)
    {
        // ЛКМ — швырнуть.
        if (lmbPressed)
        {
            LaunchGrabbed();
            EndGrab(preserveVelocity: true);
            return;
        }

        // Отпустили ПКМ — бросить без push.
        if (!rmbDown)
        {
            EndGrab();
            return;
        }
    }

    /// <summary>
    /// Крутка объекта по движению мыши (Reload зажат + physgun-режим).
    /// Камера на это время фиксируется, чтобы мышь крутила объект, а не игрока.
    /// </summary>
    private void UpdateSpin()
    {
        if (!Player.Local.IsValid() || !Player.Local.Controller.IsValid())
        {
            UnlockSpinCamera();
            return;
        }

        var controller = Player.Local.Controller;

        // Крутить можно только в physgun-режиме при активном захвате.
        bool canSpin = _mode == GrabMode.Physgun
            && _grabbed.IsValid()
            && Input.Down("Reload");

        if (!canSpin)
        {
            UnlockSpinCamera();
            return;
        }

        // Захватили камеру в момент нажатия Reload (или первого валидного кадра крутки).
        if (!_spinCameraLocked || Input.Pressed("Reload"))
        {
            _spinSavedEyeAngles = controller.EyeAngles;
            _spinCameraLocked = true;
        }

        LockSpinCamera(controller);

        bool snapping = Input.Down("Run");

        // Дельта мыши за кадр; инвертируем, чтобы вверх/вправо = крутить объект,
        // как в Facepunch-Physgun.
        var look = Input.AnalogLook * -1f;

        if (snapping)
        {
            // Снэп — крутим только по преобладающей оси.
            if (MathF.Abs(look.yaw) > MathF.Abs(look.pitch)) look.pitch = 0;
            else look.yaw = 0;
        }

        var spinRotation = Rotation.From(look) * _grabOffset;

        if (snapping)
        {
            // Конвертируем в worldspace по yaw игрока, снэпим, возвращаем обратно.
            var eyeYaw = Rotation.FromYaw(_spinSavedEyeAngles.yaw);
            var spinWorld = eyeYaw * spinRotation;
            var snapped = spinWorld.Angles().SnapToGrid(SnapAngleDegrees);
            spinRotation = eyeYaw.Inverse * Rotation.From(snapped);
        }

        _grabOffset = spinRotation;

        // Страховочный фикс: если контроллер уже успел применить look в этом кадре,
        // возвращаем сохранённый угол. На следующих кадрах UseLookControls выключен,
        // поэтому камера больше не борется с physgun за один и тот же ввод.
        controller.EyeAngles = _spinSavedEyeAngles;

        // Чтобы другие потребители (если такие есть) тоже не реагировали.
        Input.AnalogLook = default;
    }

    private void LockSpinCamera(PlayerController controller)
    {
        if (!controller.IsValid()) return;
        if (_spinLookControlsOverridden) return;

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

    // ============================ ЗАХВАТ ============================

    private void BeginPhysgunSeek()
    {
        _mode = GrabMode.PhysgunSeek;
        _beamSeekActive = true;
        TryStartGrab(GrabMode.Physgun);
    }

    private void EndSeek()
    {
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

        _grabbed = rb;
        _mode = mode;
        _beamSeekActive = false;
        EnableHeldCollisionMode(rb);

        var bodyTransform = rb.WorldTransform;

        // Если объект был заморожен — снимаем заморозку, иначе двигать его не получится.
        if (!rb.MotionEnabled)
        {
            rb.MotionEnabled = true;
        }

        if (mode == GrabMode.GravityGun)
        {
            // Gravitygun «сразу берёт к себе»: захват по центру объекта (origin),
            // мгновенное обнуление физики и телепорт к точке удержания.
            // Без телепорта тело успевает упасть/удариться об пол на первых кадрах,
            // пока скорость только-только разгоняет его к цели.
            _localOffset = Vector3.Zero;
            _grabDistance = GravityGunHoldDistance;
            _grabOffset = eye.Rotation.Inverse * bodyTransform.Rotation;

            var snapPos = origin + dir * GravityGunHoldDistance;
            rb.Velocity = Vector3.Zero;
            rb.AngularVelocity = Vector3.Zero;
            rb.WorldPosition = snapPos;
        }
        else
        {
            // Physgun: удерживаем за точку, в которую попали, на той же дистанции.
            _localOffset = bodyTransform.PointToLocal(tr.HitPosition);
            var hitDistance = Vector3.DistanceBetween(origin, tr.HitPosition);
            _grabDistance = MathF.Max(MinHoldDistance, MathF.Min(MaxHoldDistance, hitDistance));
            // Поворот объекта запоминаем относительно yaw игрока (чтобы при наклоне головы он не падал).
            var yaw = Rotation.FromYaw(Player.Local.Controller.EyeAngles.yaw);
            _grabOffset = yaw.Inverse * bodyTransform.Rotation;
        }

        return true;
    }

    private bool TryFindGrabTarget(GrabMode mode, Vector3 origin, Vector3 dir, out SceneTraceResult tr, out Rigidbody rb)
    {
        tr = TraceGrabRay(origin, dir, 0f);
        if (TryGetGrabRigidbody(tr, mode, out rb))
            return true;

        // Если центральный луч уже упёрся в мир, не даём радиусному trace'у
        // "магнититься" к соседним поверхностям и расходиться с визуальным лучом.
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

        // Ищем Rigidbody на самом GameObject или его родителях (на случай комплексных префабов).
        rb = tr.GameObject.Components.Get<Rigidbody>(FindMode.EverythingInSelfAndAncestors);
        if (!rb.IsValid())
            return false;

        // Только объекты, которыми владеет локальный игрок.
        if (rb.GameObject.Network.Owner != Connection.Local)
            return false;

        // Фильтр по типу объекта зависит от режима:
        //  * Physgun (ЛКМ) — только кастомные пропы (PropCustom). Денежные принтеры
        //    и прочие игровые сущности руками держать нельзя.
        //  * GravityGun (ПКМ) — пропы И денежные принтеры (MoneyPrinterBase).
        //    Принтер берётся только так, по требованию дизайна.
        var hasPropCustom = rb.GameObject.Components.Get<PropCustom>(FindMode.EverythingInSelfAndAncestors).IsValid();
        var hasMoneyPrinter = rb.GameObject.Components.Get<MoneyPrinterBase>(FindMode.EverythingInSelfAndAncestors).IsValid();

        if (mode == GrabMode.Physgun)
            return hasPropCustom;

        return hasPropCustom || hasMoneyPrinter;
    }

    private void EndGrab(bool preserveVelocity = false)
    {
        ResetGrab(resolvePlayerOverlap: true, preserveVelocity: preserveVelocity);
        _preventReselect = true;
    }

    private void ResetGrab(bool resolvePlayerOverlap = false, bool preserveVelocity = false)
    {
        if (resolvePlayerOverlap)
            ResolveGrabbedPlayerOverlaps(preserveVelocity);

        DisableHeldCollisionMode(_grabbed);

        _mode = GrabMode.None;
        _grabbed = null;
        _grabDistance = 0f;
        _grabOffset = Rotation.Identity;
        _localOffset = Vector3.Zero;
        _beamSeekActive = false;
        ResetBeamState();
        UnlockSpinCamera();
    }

    private void EnableHeldCollisionMode(Rigidbody rb)
    {
        if (!rb.IsValid() || !rb.GameObject.IsValid()) return;

        _grabbedHadHeldCollisionTag = rb.GameObject.Tags.Has(HeldCollisionTag);
        rb.GameObject.Tags.Add(HeldCollisionTag);
    }

    private void DisableHeldCollisionMode(Rigidbody rb)
    {
        if (rb.IsValid() && rb.GameObject.IsValid() && !_grabbedHadHeldCollisionTag)
            rb.GameObject.Tags.Remove(HeldCollisionTag);

        _grabbedHadHeldCollisionTag = false;
    }

    private void ResolveGrabbedPlayerOverlaps(bool preserveVelocity)
    {
        var rb = _grabbed;
        if (!rb.IsValid() || !rb.GameObject.IsValid()) return;

        var moved = false;
        var maxIterations = Math.Max(1, ReleaseMaxResolveIterations);

        for (var iteration = 0; iteration < maxIterations; iteration++)
        {
            if (!TryFindPlayerReleaseOffset(rb, out var offset))
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
            rb.Velocity = GetSafeReleaseDirection(rb) * ReleasePushSpeed;
    }

    private bool TryFindPlayerReleaseOffset(Rigidbody rb, out Vector3 offset)
    {
        offset = Vector3.Zero;
        var propBounds = GetRigidBodyBounds(rb);

        foreach (var go in Scene.GetAllObjects(true))
        {
            if (!go.Components.TryGet<Player>(out var player))
                continue;

            if (!player.IsValid() || !player.Controller.IsValid())
                continue;

            var playerBounds = player.Controller.BodyBox().Grow(ReleasePlayerPadding);
            if (!propBounds.Overlaps(playerBounds))
                continue;

            offset = GetHorizontalSeparationOffset(propBounds, playerBounds, ReleasePlayerPadding);
            if (offset.LengthSquared <= 0.001f)
                offset = GetFallbackReleaseDirection(rb, player) * MathF.Max(16f, ReleasePlayerPadding + 8f);

            return true;
        }

        return false;
    }

    private BBox GetRigidBodyBounds(Rigidbody rb)
    {
        if (rb.PhysicsBody is not null)
            return rb.PhysicsBody.GetBounds();

        return rb.GetWorldBounds();
    }

    private Vector3 GetHorizontalSeparationOffset(BBox propBounds, BBox playerBounds, float padding)
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

    private Vector3 GetFallbackReleaseDirection(Rigidbody rb, Player player)
    {
        var away = rb.WorldPosition - player.WorldPosition;
        away.z = 0f;

        if (away.LengthSquared > 0.001f)
            return away.Normal;

        if (Player.Local.IsValid() && Player.Local.Controller.IsValid())
        {
            var forward = Player.Local.Controller.EyeTransform.Forward;
            forward.z = 0f;
            if (forward.LengthSquared > 0.001f)
                return forward.Normal;
        }

        return Vector3.Right;
    }

    private Vector3 GetSafeReleaseDirection(Rigidbody rb)
    {
        if (!Player.Local.IsValid())
            return Vector3.Right;

        var away = rb.WorldPosition - Player.Local.WorldPosition;
        away.z = 0f;
        return away.LengthSquared > 0.001f ? away.Normal : Vector3.Right;
    }

    private Vector3 ResolveHeldPlayerOverlaps(Rigidbody rb, Vector3 desiredBodyPos)
    {
        if (!rb.IsValid())
            return desiredBodyPos;

        var currentBounds = GetRigidBodyBounds(rb);
        var desiredOffset = desiredBodyPos - rb.WorldPosition;
        var resolvedOffset = desiredOffset;
        var maxIterations = Math.Max(1, HeldMaxResolveIterations);

        for (var iteration = 0; iteration < maxIterations; iteration++)
        {
            var desiredBounds = currentBounds.Translate(resolvedOffset);
            if (!TryFindPlayerOverlapOffset(desiredBounds, MathF.Max(ReleasePlayerPadding, HeldPlayerPadding), out var pushOffset))
                break;

            resolvedOffset += pushOffset;
        }

        return rb.WorldPosition + resolvedOffset;
    }

    private bool TryFindPlayerOverlapOffset(BBox propBounds, float padding, out Vector3 offset)
    {
        offset = Vector3.Zero;

        foreach (var go in Scene.GetAllObjects(true))
        {
            if (!go.Components.TryGet<Player>(out var player))
                continue;

            if (!player.IsValid() || !player.Controller.IsValid())
                continue;

            var playerBounds = player.Controller.BodyBox().Grow(padding);
            if (!propBounds.Overlaps(playerBounds))
                continue;

            offset = GetHorizontalSeparationOffset(propBounds, playerBounds, padding);
            if (offset.LengthSquared <= 0.001f)
                offset = GetFallbackReleaseDirection(_grabbed, player) * MathF.Max(16f, padding + 8f);

            return true;
        }

        return false;
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

        var player = Player.Local;
        if (!player.IsValid() || !player.Controller.IsValid())
        {
            ResetBeamState();
            return;
        }

        var eye = player.Controller.EyeTransform;
        var start = ShotPos.IsValid() ? ShotPos.WorldPosition : eye.Position;
        var end = GetBeamEndPosition();
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
        player.SetPhysgunBeam(true, start, end, _beamBend);
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

        var visualTrace = Scene.Trace
            .Ray(eye.Position, eye.Position + eye.Forward * MaxRange)
            .IgnoreGameObjectHierarchy(player.GameObject)
            .WithoutTags("bullet", "player")
            .Run();

        if (visualTrace.Hit)
            end = ClampBeamEnd(start, visualTrace.HitPosition);

        var desiredBend = GetSeekingBeamBend(eye, start, end);
        _beamBend = LerpVector(_beamBend, desiredBend, 0.25f);
        _beamLastEnd = end;
        _beamHasLastEnd = true;
        player.SetPhysgunBeam(true, start, end, _beamBend);
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

        if (_mode == GrabMode.GravityGun)
            return _grabbed.WorldPosition;

        return _grabbed.WorldTransform.PointToWorld(_localOffset);
    }

    private Vector3 ClampBeamEnd(Vector3 start, Vector3 end)
    {
        var maxLength = MathF.Max(1f, MathF.Min(MaxRange, BeamMaxLength));
        var delta = end - start;
        if (delta.Length <= maxLength)
            return end;

        return start + delta.Normal * maxLength;
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

    private void ResetBeamState()
    {
        var hadBeam = _beamHasLastEnd;
        _beamLastEnd = Vector3.Zero;
        _beamBend = Vector3.Zero;
        _beamHasLastEnd = false;

        if (Player.Local.IsValid() && (hadBeam || Player.Local.PhysgunBeamActive))
            Player.Local.SetPhysgunBeam(false);
    }

    // ============================ ДВИЖЕНИЕ ============================

    /// <summary>
    /// Каждый FixedUpdate подталкиваем объект к целевой позиции/повороту.
    /// Поскольку мы — сетевой владелец Rigidbody, можем напрямую писать его Velocity/WorldRotation.
    /// </summary>
    private void UpdateGrabbed()
    {
        if (_mode == GrabMode.None || !_grabbed.IsValid()) return;
        if (!_grabbed.MotionEnabled) return;

        var dt = Time.Delta;
        if (dt <= 0f) return;

        var eye = Player.Local.Controller.EyeTransform;
        var grabDistance = MathF.Max(MinHoldDistance, MathF.Min(MaxHoldDistance, _grabDistance));
        var targetPos = eye.Position + eye.Forward * grabDistance;

        // Целевой поворот:
        // * gravitygun — следует полному взгляду (можно поднять предмет и над собой);
        // * physgun — следует только yaw игрока, чтобы предмет не «колбасило» при наклоне головы.
        Rotation targetRot;
        if (_mode == GrabMode.GravityGun)
            targetRot = eye.Rotation * _grabOffset;
        else
            targetRot = Rotation.FromYaw(Player.Local.Controller.EyeAngles.yaw) * _grabOffset;

        // Хотим, чтобы локальная точка _localOffset на теле оказалась в targetPos:
        // worldPoint = bodyPos + bodyRot * localOffset  =>  bodyPos = targetPos - targetRot * localOffset.
        var offsetWorld = targetRot * _localOffset;
        var desiredBodyPos = targetPos - offsetWorld;

        var rb = _grabbed;
        desiredBodyPos = ResolveHeldPlayerOverlaps(rb, desiredBodyPos);

        // Поворачиваем напрямую (плавно), угловую скорость зануляем — иначе физика будет дёргать.
        rb.WorldRotation = Rotation.Slerp(rb.WorldRotation, targetRot, MathF.Min(1f, RotationLerp));
        rb.AngularVelocity = Vector3.Zero;

        // Линейно — задаём скорость, которая за следующий шаг приведёт тело в desiredBodyPos.
        // Это сохраняет физическое взаимодействие (объект всё ещё может толкать другие тела).
        var posDelta = desiredBodyPos - rb.WorldPosition;
        var v = posDelta / dt;
        if (v.Length > MaxLinearSpeed) v = v.Normal * MaxLinearSpeed;
        rb.Velocity = v;
    }

    // ============================ ДЕЙСТВИЯ ============================

    /// <summary>Швырнуть объект вперёд (gravitygun + ЛКМ).</summary>
    private void LaunchGrabbed()
    {
        if (!_grabbed.IsValid()) return;
        if (_grabbed.GameObject.Network.Owner != Connection.Local) return;

        var dir = Player.Local.Controller.EyeTransform.Forward;
        var mass = MathF.Max(0.1f, _grabbed.Mass);
        _grabbed.ApplyImpulse(dir * (LaunchForce * mass));
    }

    /// <summary>
    /// Заморозить объект (physgun + ПКМ): отключаем его физику.
    /// Локальный игрок — владелец, поэтому может это сделать напрямую и изменение
    /// разойдётся по сети штатным механизмом синхронизации Rigidbody.
    /// </summary>
    private void FreezeGrabbed()
    {
        if (!_grabbed.IsValid()) return;
        if (_grabbed.GameObject.Network.Owner != Connection.Local) return;

        _grabbed.Velocity = Vector3.Zero;
        _grabbed.AngularVelocity = Vector3.Zero;
        _grabbed.MotionEnabled = false;
    }
}
