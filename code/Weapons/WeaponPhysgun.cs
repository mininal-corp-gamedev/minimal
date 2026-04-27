using Sandbox;

namespace Minimal.Weapons;

/// <summary>
/// MP5 — автомат, стрельба очередью, средний урон и скорострельность.
/// </summary>
public sealed class WeaponPhysgun : Weapon
{
    [Property, Category("Ammo")] public override int ClipSize { get; set; } = 30;
    [Property, Category("Ammo")] public override int TotalReserveAmmo { get; set; } = 90;
    [Property, Category("Combat")] public override float FireDelay { get; set; } = 0.06f;
    [Property, Category("Combat")] public override float Damage { get; set; } = 8f;
    [Property, Category("Combat")] public override bool SemiAuto { get; set; } = false;
    [Property, Category("Combat")] public override bool HasHipFire { get; set; } = true;
    [Property, Category("Combat")] public override bool CanAttackWithoutAmmo { get; set; } = false;
    [Property, Category("Reload")] public override bool HasReload { get; set; } = true;
    [Property, Category("Reload")] public override float ReloadDurationSeconds { get; set; } = 2f;

    protected override void OnWeaponStart()
    {
        Ammo = ClipSize;
        Log.Info("[WeaponMp5] Ready");
    }
}
