using Sandbox;
using System;

/// <summary>
/// Кирка / ближний бой: без патронов и перезарядки, удар лучом без снарядов.
/// Урановый меч наносит splash-урон в радиусе вокруг точки попадания.
/// </summary>
public sealed class WeaponSwordUranium : Weapon
{
    [Property, Category("Combat")] public override bool IsMelee { get; set; } = true;
    [Property, Category("Reload")] public override bool HasReload { get; set; } = false;
    [Property, Category("Combat")] public override bool SemiAuto { get; set; } = true;
    [Property, Category("Combat")] public override float FireDelay { get; set; } = 0.35f;
    [Property, Category("Combat")] public override float Damage { get; set; } = 55f;
    [Property, Category("Combat")] public override float AttackRange { get; set; } = 90f;

    /// <summary>Радиус splash-урона вокруг точки попадания.</summary>
    [Property, Category("Splash")] public float SplashRadius { get; set; } = 120f;

    /// <summary>Максимальный splash-урон (на нулевой дистанции). Линейно затухает к краю радиуса.</summary>
    [Property, Category("Splash")] public float SplashDamage { get; set; } = 30f;

    protected override void PerformFire()
    {
        if ( !CanFire() ) return;

        _firedThisFrame = true;
        State = WeaponState.Fire;
        _timeUntilNextFire = FireDelay;

        var origin = ShotPos != null ? ShotPos.WorldPosition : WorldPosition;
        var direction = GetFireDirection();
        var tr = DoTrace( origin, direction );

        // Основной урон по цели
        ApplyDamageToTrace( tr, Damage );

        // Splash-урон: точка центра — место попадания (или конец луча)
        var splashCenter = tr.Hit ? tr.HitPosition : (origin + direction.Normal * AttackRange);
        ApplySplashDamage( splashCenter, tr.GameObject );

        Player.Local?.RpcOnWeaponFired( FireSound, origin, null, origin, GameObject.WorldRotation, null );
    }

    /// <summary>
    /// Находит все объекты в радиусе splash и наносит им урон с линейным затуханием по расстоянию.
    /// Пропускает владельца и объект, по которому уже прошёл основной удар.
    /// </summary>
    private void ApplySplashDamage( Vector3 center, GameObject primaryTarget )
    {
        if ( SplashDamage <= 0f || SplashRadius <= 0f ) return;

        var localPlayer = Player.Local;

        foreach ( var obj in Scene.FindInPhysics( new Sphere( center, SplashRadius ) ) )
        {
            if ( !obj.IsValid() ) continue;

            // Не бьём себя
            if ( localPlayer.IsValid() && obj == localPlayer.GameObject ) continue;
            if ( localPlayer.IsValid() && obj.IsDescendant( localPlayer.GameObject ) ) continue;

            // Не дублируем урон по основной цели
            if ( primaryTarget.IsValid() && (obj == primaryTarget || obj.IsDescendant( primaryTarget ) || primaryTarget.IsDescendant( obj )) )
                continue;

            var distance = center.Distance( obj.WorldPosition );
            if ( distance > SplashRadius ) continue;

            // Линейное затухание: 100% на центре → 0% на краю радиуса
            var falloff = 1f - (distance / SplashRadius);
            var dmg = SplashDamage * falloff;
            if ( dmg <= 0f ) continue;

            // Игрок
            if ( obj.Components.TryGet<Player>( out var hitPlayer, FindMode.EverythingInSelfAndParent ) )
            {
                //hitPlayer.TakeDamageFromWeapon( dmg, localPlayer );
                continue;
            }

            // IDamageable (Enemy, Clone, Ore и т.д.)
            if ( obj.Components.TryGet<IDamageable>( out var damageable, FindMode.EverythingInSelfAndParent ) )
            {
                damageable.OnDamage( new DamageInfo
                {
                    Attacker = localPlayer?.GameObject,
                    Weapon = GameObject,
                    Position = center,
                    Damage = dmg
                } );
                continue;
            }
        }
    }
}
