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
    [Property, Category("Physgun")] public float MaxRange { get; set; } = 1024f;
    [Property, Category("Physgun")] public float MinHoldDistance { get; set; } = 60f;
    [Property, Category("Physgun")] public float MaxHoldDistance { get; set; } = 1024f;
    [Property, Category("Physgun")] public float GravityGunHoldDistance { get; set; } = 90f;
    [Property, Category("Physgun")] public float ScrollStep { get; set; } = 25f;
    [Property, Category("Physgun")] public float MaxLinearSpeed { get; set; } = 4000f;
    [Property, Category("Physgun")] public float LaunchForce { get; set; } = 1500f;
    [Property, Category("Physgun")] public float SnapAngleDegrees { get; set; } = 45f;
    [Property, Category("Physgun")] public float RotationLerp { get; set; } = 0.5f;

    // Physgun не использует систему патронов и перезарядку.
    protected override bool UseDefaultCombatInput => false;

    private enum GrabMode
    {
        None,
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

    protected override void OnWeaponStart()
    {
        HoldType = WeaponHoldType.PhysGun;
        Ammo = 0;
        Log.Info("[WeaponPhysgun] Ready");
    }

    protected override void OnDisabled()
    {
        ResetGrab();
    }

    protected override void OnWeaponFixedUpdate()
    {
        if (!Player.Local.IsValid()) return;
        if (Player.Local.IsArrested) { ResetGrab(); return; }
        if (!Player.Local.Controller.IsValid()) return;

        ValidateGrabbed();
        HandleInput();
        UpdateGrabbed();
    }

    private void ValidateGrabbed()
    {
        if (_mode == GrabMode.None) return;

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
                TryStartGrab(GrabMode.Physgun);
                return;
            }

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

        // Reload — крутить объект.
        if (Input.Down("Reload"))
        {
            HandleSpin(snapping: Input.Down("Run"));
        }
    }

    private void HandleGravityGunInput(bool rmbDown, bool lmbPressed)
    {
        // ЛКМ — швырнуть.
        if (lmbPressed)
        {
            LaunchGrabbed();
            EndGrab();
            return;
        }

        // Отпустили ПКМ — бросить без push.
        if (!rmbDown)
        {
            EndGrab();
            return;
        }
    }

    /// <summary>Накапливаем поворот объекта по AnalogLook (как у физгана в s&amp;box-sandbox).</summary>
    private void HandleSpin(bool snapping)
    {
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
            var eyeYaw = Rotation.FromYaw(Player.Local.Controller.EyeAngles.yaw);
            var spinWorld = eyeYaw * spinRotation;
            var snapped = spinWorld.Angles().SnapToGrid(SnapAngleDegrees);
            spinRotation = eyeYaw.Inverse * Rotation.From(snapped);
        }

        _grabOffset = spinRotation;
    }

    // ============================ ЗАХВАТ ============================

    private bool TryStartGrab(GrabMode mode)
    {
        var eye = Player.Local.Controller.EyeTransform;
        var origin = eye.Position;
        var dir = eye.Forward;

        var tr = Scene.Trace
            .Ray(origin, origin + dir * MaxRange)
            .IgnoreGameObjectHierarchy(Player.Local.GameObject)
            .WithoutTags("bullet", "player")
            .Run();

        if (!tr.Hit) return false;
        if (!tr.GameObject.IsValid()) return false;

        // Ищем Rigidbody на самом GameObject или его родителях (на случай комплексных префабов).
        var rb = tr.GameObject.Components.Get<Rigidbody>(FindMode.EverythingInSelfAndAncestors);
        if (!rb.IsValid()) return false;

        // Только объекты, которыми владеет локальный игрок.
        if (rb.GameObject.Network.Owner != Connection.Local) return false;

        _grabbed = rb;
        _mode = mode;

        // Точка захвата в локальных координатах объекта.
        var bodyTransform = rb.WorldTransform;
        _localOffset = bodyTransform.PointToLocal(tr.HitPosition);

        if (mode == GrabMode.GravityGun)
        {
            // Подтягиваем близко, поворот объекта запоминаем относительно полного взгляда.
            _grabDistance = GravityGunHoldDistance;
            _grabOffset = eye.Rotation.Inverse * bodyTransform.Rotation;
        }
        else
        {
            // Physgun: удерживаем на той же дистанции, где попали.
            var hitDistance = Vector3.DistanceBetween(origin, tr.HitPosition);
            _grabDistance = MathF.Max(MinHoldDistance, MathF.Min(MaxHoldDistance, hitDistance));
            // Поворот объекта запоминаем относительно yaw игрока (чтобы при наклоне головы он не падал).
            var yaw = Rotation.FromYaw(Player.Local.Controller.EyeAngles.yaw);
            _grabOffset = yaw.Inverse * bodyTransform.Rotation;
        }

        // Если объект был заморожен — снимаем заморозку, иначе двигать его не получится.
        if (!rb.MotionEnabled)
        {
            rb.MotionEnabled = true;
        }

        return true;
    }

    private void EndGrab()
    {
        ResetGrab();
        _preventReselect = true;
    }

    private void ResetGrab()
    {
        _mode = GrabMode.None;
        _grabbed = null;
        _grabDistance = 0f;
        _grabOffset = Rotation.Identity;
        _localOffset = Vector3.Zero;
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

