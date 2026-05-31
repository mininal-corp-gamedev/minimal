using Ambi.Utils;
using Sandbox;
using Sandbox.Citizen;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

/// <summary>
/// Базовый класс оружия для 3D шутера. Наследуйся от него в конкретных классах (WeaponMp5, WeaponShotgun и т.д.)
/// и переопределяй параметры и виртуальные методы для своей логики.
/// </summary>
public class Weapon : Component
{
    [Property] public GameObject ShotPos { get; set; }
    [Property] public SkinnedModelRenderer Viewmodel { get; set; }

    /// <summary>Префаб пули для этого оружия. Если не задан — берётся из WeaponManager.BulletPrefab.</summary>
    [Property, Category("Combat")] public GameObject BulletPrefab { get; set; }

    /// <summary>Префаб спрайта вспышки при выстреле. Спавнится в ShotPos.</summary>
    [Property, Category("Combat")] public GameObject SpriteFirePrefab { get; set; }
    [Property, Category("Combat")] public bool SpawnSpriteFireOnShot { get; set; } = false;
    [Property, Category("Combat")] public CitizenAnimationHelper.HoldTypes HoldType { get; set; } = CitizenAnimationHelper.HoldTypes.None;
    [Property, Category("Combat")] public WeaponHandedness HoldTypeHandedness { get; set; } = WeaponHandedness.BothHands;

    public WeaponState State { get; protected set; } = WeaponState.None;

    [Property, Category("Sounds")] public SoundEvent FireSound { get; set; }
    [Property, Category("Sounds")] public SoundEvent ReloadSound { get; set; }

    [Property, Category("Ammo"), ReadOnly] public int Ammo { get; set; } = 0;
    [Property, Category("Ammo")] public virtual int ClipSize { get; set; } = 30;
    [Property, Category("Ammo")] public virtual int TotalReserveAmmo { get; set; } = 100;

    /// <summary>Ближний бой: не тратит патроны, нет перезарядки, не спавнит пули из менеджера.</summary>
    [Property, Category("Combat")] public virtual bool IsMelee { get; set; } = false;

    /// <summary>Можно ли атаковать без патронов (например, нож у огнестрельного набора).</summary>
    [Property, Category("Combat")] public virtual bool CanAttackWithoutAmmo { get; set; } = false;

    /// <summary>Используется ли система патронов (магазин + резерв) для этой единицы оружия.</summary>
    public bool UsesAmmunition => !IsMelee && !CanAttackWithoutAmmo;

    /// <summary>Задержка между выстрелами в секундах.</summary>
    [Property, Category("Combat")] public virtual float FireDelay { get; set; } = 0.06f;

    [Property, Category("Combat")] public virtual float Damage { get; set; } = 8f;
    [Property, Category("Combat")] public List<string> BulletPassThroughTags { get; set; } = BulletCollisionRules.CreateDefaultPassThroughTags();
    [Property, Category("Combat")] public int BulletPassThroughLimit { get; set; } = 8;
    [Property, Category("Combat")] public float BulletPenetrationStep { get; set; } = 1f;

    /// <summary>Максимальная дальность луча атаки (урон по попаданию; для снарядов — конец луча при промахе).</summary>
    [Property, Category("Combat")] public virtual float AttackRange { get; set; } = 2048f;

    /// <summary>Есть ли стрельба от бедра (без прицела).</summary>
    [Property, Category("Combat")] public virtual bool HasHipFire { get; set; } = true;

    /// <summary>Одиночный выстрел по нажатию, иначе — по зажатию.</summary>
    [Property, Category("Combat")] public virtual bool SemiAuto { get; set; } = false;

    /// <summary>Есть ли перезарядка.</summary>
    [Property, Category("Reload")] public virtual bool HasReload { get; set; } = true;

    /// <summary>Длительность перезарядки в секундах.</summary>
    [Property, Category("Reload")] public virtual float ReloadDurationSeconds { get; set; } = 2f;

    [Property] public bool IsIronSight { get; set; } = false;

    /// <summary>Префаб WorldModel для отображения оружия у других игроков. Если не задан — WorldModel не отображается.</summary>
    [Property, Category("WorldModel")] public GameObject WorldModelPrefab { get; set; }

    /// <summary>
    /// Использовать ли стандартный обработчик ввода (огонь/перезарядка/прицеливание).
    /// Спец-оружие вроде Physgun/Toolgun возвращает false и реализует свой ввод
    /// в своих update/fixed-update хуках.
    /// </summary>
    protected virtual bool UseDefaultCombatInput => true;

    protected bool _isReloading = false;
    private bool _isPaused = false;
    private bool _wasJustResumed = false;
    private bool _attack1WasReleased = true;
    private bool _attack2WasReleased = true;
    private bool _reloadWasReleased = true;
    private bool _viewmodelRenderingPrepared = false;

    /// <summary>True только в тот кадр, когда реально был выстрел (для b_attack во viewmodel).</summary>
    protected bool _firedThisFrame = false;

    protected TimeUntil _timeUntilReloadDone;
    protected TimeUntil _timeUntilNextFire;

    /// <summary>Длина клипа перезарядки в animgraph в секундах. Скорость анимации = это / ReloadDurationSeconds. В animgraph добавь float-параметр "reload_speed" и умножь на него скорость воспроизведения анимации перезарядки.</summary>
    protected virtual float GetReloadAnimClipDuration() => 1f;

    protected virtual void OnWeaponAwake() { }
    protected virtual void OnWeaponStart() { }
    protected virtual void OnWeaponFixedUpdate() { }
    protected virtual void OnWeaponUpdate() { }
    protected virtual int GetHoldTypeAttack() => (int)WeaponHandedness.BothHands;

    /// <summary>Хук на ПКМ для ближнего боя (ironsights в этом случае не используются).</summary>
    protected virtual void OnSecondaryAttack() { }

    protected virtual void HandleDefaultCombatInput()
    {
        if (!IsMelee && Input.Pressed("Reload") && _reloadWasReleased)
            Reload();

        bool wantFire = SemiAuto ? Input.Pressed("Attack1") : (Input.Down("Attack1") && _attack1WasReleased);
        if (wantFire)
            PerformFire();

        if (IsMelee && Input.Pressed("Attack2"))
            OnSecondaryAttack();

        if (IsMelee)
            IsIronSight = false;
        else if (Input.Down("Attack2") && !Input.Down("Run") && _attack2WasReleased)
            IsIronSight = true;
        else
            IsIronSight = false;
    }

    private void ConfigureViewmodelRendering()
    {
        var allRenderersReady = true;

        foreach (var renderer in GameObject.Components.GetAll<ModelRenderer>(FindMode.EverythingInSelfAndDescendants))
        {
            if (!renderer.IsValid())
                continue;

            if (renderer.SceneObject is null)
            {
                allRenderersReady = false;
                continue;
            }

            renderer.SceneObject.Flags.CastShadows = false;

            if (renderer is SkinnedModelRenderer skinnedRenderer && skinnedRenderer.SceneModel is not null)
                skinnedRenderer.SceneModel.Flags.CastShadows = false;
        }

        _viewmodelRenderingPrepared = allRenderersReady;
    }

    protected override void OnAwake()
    {
        Log.Trace($"[Weapon] OnAwake {GameObject.Name}");
        OnWeaponAwake();
    }

    protected override void OnStart()
    {
        Log.Info($"[Weapon] OnStart {GameObject.Name}, ClipSize={ClipSize}, Damage={Damage}");
        ConfigureViewmodelRendering();
        OnWeaponStart();
    }

    protected override void OnEnabled()
    {
        _viewmodelRenderingPrepared = false;
        ConfigureViewmodelRendering();
        ResetViewmodelState();
    }

    /// <summary>Сброс параметров viewmodel при включении оружия (смена оружия), чтобы не проигрывались старые b_reload/b_attack.</summary>
    protected virtual void ResetViewmodelState()
    {
        if (Viewmodel == null) return;
        Viewmodel.Set("b_reload", false);
        Viewmodel.Set("b_reloading", false);
        Viewmodel.Set("b_attack", false);
        Viewmodel.Set("holdtype_attack", GetHoldTypeAttack());
    }

    protected override void OnFixedUpdate()
    {
        CheckDelayReload();
        StateFixedUpdate();
        ViewmodelFixedUpdate();
        OnWeaponFixedUpdate();
    }

    protected override void OnUpdate()
    {
        if (!_viewmodelRenderingPrepared)
            ConfigureViewmodelRendering();

        OnWeaponUpdate();
    }

    /// <summary>Можно ли сейчас произвести выстрел. Переопределяй в наследниках для своей логики.</summary>
    protected virtual bool CanFire()
    {
        if (Player.Local is null || !Player.Local.IsAlive) return false;
        if (!_timeUntilNextFire) return false;
        if (State == WeaponState.Reload) return false;
        if (UsesAmmunition && Ammo <= 0) return false;
        return true;
    }

    /// <summary>Один выстрел: трассер, урон, звук, расход патронов. Переопределяй для дробовика и т.д.</summary>
    protected virtual void PerformFire()
    {
        if (!CanFire()) return;

        _firedThisFrame = true;
        State = WeaponState.Fire;
        _timeUntilNextFire = FireDelay;

        if (UsesAmmunition)
            Ammo -= 1;

        if (UsesAmmunition && Ammo <= 0)
            State = WeaponState.None;

        var origin = ShotPos != null ? ShotPos.WorldPosition : WorldPosition;
        var direction = GetFireDirection();

        ApplyDamageAlongTrace(origin, direction, Damage);

        var bullet = SpawnBullet(origin, direction);

        var muzzlePrefab = (SpawnSpriteFireOnShot && SpriteFirePrefab.IsValid()) ? SpriteFirePrefab : null;
        var muzzleRot = ShotPos != null ? ShotPos.WorldRotation : GameObject.WorldRotation;
        Player.Local?.RpcOnWeaponFired(FireSound, origin, muzzlePrefab, origin, muzzleRot, bullet, GetHoldTypeAttack());
    }

    /// <summary>Префаб пули: свой BulletPrefab или из WeaponManager.</summary>
    protected GameObject GetBulletPrefab()
    {
        if (BulletPrefab.IsValid()) return BulletPrefab;
        return WeaponManager.Instance?.BulletPrefab;
    }

    /// <summary>Спавн одного снаряда из origin в направлении direction. Вызывается из PerformFire и из дробовика на каждый трейс.</summary>
    protected virtual GameObject SpawnBullet(Vector3 origin, Vector3 direction)
    {
        if (IsMelee) return null;
        var prefab = GetBulletPrefab();
        if (!prefab.IsValid() || direction.LengthSquared < 0.0001f) return null;
        var bulletObj = prefab.Clone(origin, Rotation.LookAt(direction));

        if (bulletObj.Components.TryGet<Bullet>(out var bullet))
        {
            bullet.Direction = direction;
            bullet.WorldRotation = Rotation.LookAt(direction);
            bullet.PassThroughTags = BulletCollisionRules.CloneTags(BulletPassThroughTags);
            bullet.MaxPassThroughHitsPerMove = BulletPassThroughLimit;
            bullet.HitAdvanceDistance = BulletPenetrationStep;
            // Пробрасываем информацию об оружии и владельце в пулю,
            // чтобы при необходимости можно было собрать корректный DamageInfo.
            bullet.Owner = Player.Local?.GameObject;
            bullet.Weapon = GameObject;
        }

        return bulletObj;
    }

    /// <summary>Спавн спрайта вспышки при выстреле (позиция ShotPos), если включено SpawnSpriteFireOnShot. Родитель — GlobalManager.Instance.CurrentLevel, через 0.5 сек объект уничтожается.</summary>
    protected virtual void SpawnSpriteFire()
    {
        if (!SpawnSpriteFireOnShot || !SpriteFirePrefab.IsValid()) return;
        var pos = ShotPos != null ? ShotPos.WorldPosition : WorldPosition;
        var rot = ShotPos != null ? ShotPos.WorldRotation : GameObject.WorldRotation;
        var obj = SpriteFirePrefab.Clone(pos, rot);

        var destroy = obj.Components.Create<DestroyAfterSeconds>();
        destroy.Seconds = 0.5f;
    }

    /// <summary>Направление выстрела (по умолчанию — взгляд игрока).</summary>
    protected virtual Vector3 GetFireDirection()
    {
        if (!Player.Local?.Controller.IsValid() ?? true) return GameObject.WorldRotation.Forward;
        return Player.Local.Controller.EyeTransform.Forward;
    }

    /// <summary>Один луч трассировки. Для дробовика переопредели и вызывай несколько раз со спредом.</summary>
    protected virtual SceneTraceResult DoTrace(Vector3 origin, Vector3 direction)
    {
        var dir = direction.Normal;
        var range = Math.Max(AttackRange, 1f);

        return DoTraceSegment(origin, origin + dir * range, null);
    }

    protected virtual void ApplyDamageAlongTrace(Vector3 origin, Vector3 direction, float damage)
    {
#if SERVER
        if (damage <= 0) return;
        if (direction.LengthSquared < 0.0001f) return;

        var dir = direction.Normal;
        var range = Math.Max(AttackRange, 1f);
        var end = origin + dir * range;
        var traceStart = origin;
        var ignoredPassThroughObjects = new List<GameObject>();
        var maxPassThroughHits = Math.Max(BulletPassThroughLimit, 0);
        var passThroughHits = 0;

        while (true)
        {
            var tr = DoTraceSegment(traceStart, end, ignoredPassThroughObjects);
            if (!tr.Hit) return;

            ApplyDamageToTrace(tr, damage);

            if (!BulletCollisionRules.TryGetPassThroughRoot(BulletCollisionRules.GetHitObject(tr), BulletPassThroughTags, out var passThroughRoot))
                return;

            if (passThroughHits >= maxPassThroughHits)
                return;

            AddIgnoredPassThroughObject(ignoredPassThroughObjects, passThroughRoot);
            passThroughHits++;

            traceStart = tr.HitPosition + dir * MathF.Max(BulletPenetrationStep, 0.01f);
            if ((end - traceStart).LengthSquared <= 0.01f)
                return;
        }
#endif
    }

    private SceneTraceResult DoTraceSegment(Vector3 start, Vector3 end, IReadOnlyList<GameObject> ignoredObjects)
    {
        var trace = Scene.Trace
            .Ray(start, end)
            .WithoutTags("bullet");

        if (Player.Local?.GameObject.IsValid() == true)
            trace = trace.IgnoreGameObjectHierarchy(Player.Local.GameObject);

        if (ignoredObjects is not null)
        {
            foreach (var ignored in ignoredObjects)
            {
                if (ignored.IsValid())
                    trace = trace.IgnoreGameObjectHierarchy(ignored);
            }
        }

        return trace.Run();
    }

    private static void AddIgnoredPassThroughObject(List<GameObject> ignoredObjects, GameObject passThroughRoot)
    {
        if (!passThroughRoot.IsValid()) return;

        foreach (var ignored in ignoredObjects)
        {
            if (ignored == passThroughRoot)
                return;
        }

        ignoredObjects.Add(passThroughRoot);
    }

    /// <summary>Нанести урон по результату трассировки. Вызывается из PerformFire.</summary>
    protected virtual void ApplyDamageToTrace(SceneTraceResult tr, float damage)
    {
#if SERVER
        if (!tr.Hit || damage <= 0) return;

        if (Player.Local?.IsSafezone == true) return;

        var go = tr.GameObject;
        while (go.IsValid())
        {
            if (go.Components.TryGet<Player>(out var hitPlayer, FindMode.EverythingInSelfAndParent))
            {
                hitPlayer.TakeDamageFromWeapon(
                    damage,
                    Player.Local.GameObject,
                    damagePosition: tr.HitPosition,
                    damageOrigin: tr.StartPosition
                );

                if (Player.Local.IsValid() && Player.Local.HitSound.IsValid())
                    Sound.Play(Player.Local.HitSound);

                return;
            }

            if (tr.Component is Component.IDamageable componentDamageable)
            {
                componentDamageable.OnDamage(new DamageInfo
                {
                    Attacker = Player.Local?.GameObject,
                    Weapon = GameObject,
                    Position = tr.HitPosition,
                    Origin = tr.StartPosition,
                    Shape = tr.Shape,
                    Damage = damage
                });

                return;
            }

            if (go.Components.TryGet<ICustomDamagable>(out var damageable, FindMode.EverythingInSelfAndParent))
            {
                damageable.OnDamage(new DamageInfo
                {
                    Attacker = Player.Local?.GameObject,
                    Weapon = GameObject,
                    Position = tr.HitPosition,
                    Origin = tr.StartPosition,
                    Damage = damage
                });

                return;
            }

            go = go.Parent;
        }
#endif
    }


    public virtual void Reload()
    {
        if (IsMelee) return;
        if (!HasReload) return;
        if (!_timeUntilReloadDone) return;
        if (Ammo >= ClipSize) return;
        if (_isReloading) return;
        if (TotalReserveAmmo <= 0) return;

        _isReloading = true;
        State = WeaponState.Reload;
        _timeUntilReloadDone = ReloadDurationSeconds;

        _ = PlayReloadSoundAsync();
        Log.Info($"[Weapon] Reload started, duration={ReloadDurationSeconds}s");

        OnReloadStarted();
    }

    /// <summary>Вызывается при старте перезарядки. По умолчанию ставит b_reload = true. Для револьвера можно только один раз триггернуть анимацию.</summary>
    protected virtual void OnReloadStarted()
    {
        if (Viewmodel != null)
            Viewmodel.Set("b_reload", true);
        Player.Local?.RpcSetPlayerReload(true);
    }

    /// <summary>Вызывается по окончании перезарядки. По умолчанию переключает b_reload (GetBool → инверт), чтобы animgraph завершил анимацию; сбрасывает b_reloading. Револьвер переопределяет и ничего не ставит.</summary>
    protected virtual void OnReloadFinished()
    {
        if (Viewmodel == null) return;
        Viewmodel.Set("b_reload", !Viewmodel.GetBool("b_reload"));
        Viewmodel.Set("b_reloading", false);
        Player.Local?.RpcSetPlayerReload(false);
    }

    private async Task PlayReloadSoundAsync()
    {
        await Task.DelaySeconds(0f);
        if (ReloadSound.IsValid())
            Sound.Play(ReloadSound);
    }

    private void CheckDelayReload()
    {
        if (_isPaused) return;
        if (!_timeUntilReloadDone || !_isReloading) return;

        OnReloadFinished();

        State = WeaponState.None;
        _isReloading = false;
        var toAdd = Math.Min(ClipSize - Ammo, TotalReserveAmmo);
        Ammo += toAdd;
        TotalReserveAmmo -= toAdd;
        Log.Info($"[Weapon] Reload finished, Ammo={Ammo}");
    }

    private void StateFixedUpdate()
    {
        if (_isPaused) return;

        // Заблокировать управление оружием для арестованного игрока.
        if (Player.Local?.IsArrested == true) return;

        if (_wasJustResumed)
        {
            if (!Input.Down("Attack1")) _attack1WasReleased = true;
            if (!Input.Down("Attack2")) _attack2WasReleased = true;
            if (!Input.Down("Reload")) _reloadWasReleased = true;
            if (_attack1WasReleased && _attack2WasReleased && _reloadWasReleased)
                _wasJustResumed = false;
        }

        if (!UseDefaultCombatInput) return;

        HandleDefaultCombatInput();
    }

    private void ViewmodelFixedUpdate()
    {
        if (Viewmodel == null) return;

        bool isWalking = (Input.Down("Forward") || Input.Down("Backward") || Input.Down("Left") || Input.Down("Right"))
            && Player.Local?.Controller.WishVelocity.Length > 0.1f;
        bool isRunning = Input.Down("Run") && _firedThisFrame == false && Player.Local?.Controller.WishVelocity.Length > 0.1f;
        bool isShooting = _firedThisFrame;

        if (isWalking) Viewmodel.Set("move_bob", .45f);
        else Viewmodel.Set("move_bob", 0f);
        if (isRunning) Viewmodel.Set("b_sprint", true);
        else Viewmodel.Set("b_sprint", false);
        if (isShooting)
        {
            Viewmodel.Set("holdtype_attack", GetHoldTypeAttack());
            Viewmodel.Set("b_attack", true);
        }
        Viewmodel.Set("ironsights", IsIronSight ? 1 : 0);

        _firedThisFrame = false;
    }

    public void OnPause()
    {
        _isPaused = true;
        IsIronSight = false;
        _isReloading = false;
        State = WeaponState.None;
        Log.Info("[Weapon] Weapon paused and states reset");
    }

    public void OnResume()
    {
        _isPaused = false;
        _wasJustResumed = true;
        _attack1WasReleased = false;
        _attack2WasReleased = false;
        _reloadWasReleased = false;
        Log.Info("[Weapon] Weapon resumed, waiting for button release");
    }
}

public enum WeaponState
{
    None,
    Fire,
    Reload
}

public enum WeaponHandedness
{
    /// <summary>Обе руки (0).</summary>
    BothHands = 0,
    /// <summary>Правая рука (1).</summary>
    RightHand = 1,
    /// <summary>Левая рука (2).</summary>
    LeftHand = 2
}
