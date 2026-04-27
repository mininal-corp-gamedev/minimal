using Sandbox;
using System;

namespace Minimal.Weapons;

/// <summary>
/// Дробовик — за один выстрел выпускает несколько дробинок со спредом, каждая наносит урон.
/// </summary>
public sealed class WeaponShotgun : Weapon
{
    [Property, Category("Shotgun")] public int PelletCount { get; set; } = 8;
    [Property, Category("Shotgun")] public float SpreadDegrees { get; set; } = 4f;

    [Property, Category("Ammo")] public override int ClipSize { get; set; } = 6;
    [Property, Category("Ammo")] public override int TotalReserveAmmo { get; set; } = 24;
    [Property, Category("Combat")] public override float FireDelay { get; set; } = 0.9f;
    [Property, Category("Combat")] public override float Damage { get; set; } = 10f;
    [Property, Category("Combat")] public override bool SemiAuto { get; set; } = false;
    [Property, Category("Combat")] public override bool HasHipFire { get; set; } = true;
    [Property, Category("Combat")] public override bool CanAttackWithoutAmmo { get; set; } = false;
    [Property, Category("Reload")] public override bool HasReload { get; set; } = true;
    [Property, Category("Reload")] public override float ReloadDurationSeconds { get; set; } = 3f;

    /// <summary>Длина клипа b_reload в animgraph (сек). Скорость анимации = это значение / ReloadDurationSeconds.</summary>
    [Property, Category("Reload")] public float ReloadAnimClipDuration { get; set; } = 1f;
    protected override float GetReloadAnimClipDuration() => ReloadAnimClipDuration;

    protected override void OnWeaponStart()
    {
        Ammo = ClipSize;
        Log.Info("[WeaponShotgun] Ready");
    }

    protected override void PerformFire()
    {
        if (!CanFire()) return;

        _firedThisFrame = true;
        State = WeaponState.Fire;
        _timeUntilNextFire = FireDelay;

        if (UsesAmmunition)
            Ammo -= 1;

        if (FireSound.IsValid())
            Sound.Play(FireSound, ShotPos?.WorldPosition ?? WorldPosition);

        if (UsesAmmunition && Ammo <= 0)
            State = WeaponState.None;

        var origin = ShotPos != null ? ShotPos.WorldPosition : WorldPosition;
        var baseDir = GetFireDirection();

        // Один трейс строго по центру, остальные со спредом по бокам
        for (int i = 0; i < PelletCount; i++)
        {
            var direction = i == 0 ? baseDir : ApplySpread(baseDir, SpreadDegrees);
            var tr = DoTrace(origin, direction);
            ApplyDamageToTrace(tr, Damage);
            SpawnBullet(origin, direction);
        }

        SpawnSpriteFire();
    }

    private static Vector3 ApplySpread(Vector3 direction, float spreadDegrees)
    {
        if (spreadDegrees <= 0) return direction;
        var yaw = (Random.Shared.Float() - 0.5f) * 2f * spreadDegrees;
        var pitch = (Random.Shared.Float() - 0.5f) * 2f * spreadDegrees;
        var rot = Rotation.FromYaw(yaw) * Rotation.FromPitch(pitch);
        return (rot * direction).Normal;
    }
}
