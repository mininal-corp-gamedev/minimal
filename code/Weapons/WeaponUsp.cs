using Sandbox;

namespace Minimal.Weapons;

/// <summary>
/// USP — пистолет, одиночные выстрелы или короткая очередь, малый урон, быстрая перезарядка.
/// </summary>
public sealed class WeaponUsp : Weapon
{
    [Property, Category("Ammo")] public override int ClipSize { get; set; } = 12;
    [Property, Category("Ammo")] public override int TotalReserveAmmo { get; set; } = 48;
    [Property, Category("Combat")] public override float FireDelay { get; set; } = 0.15f;
    [Property, Category("Combat")] public override float Damage { get; set; } = 6f;
    [Property, Category("Combat")] public override bool SemiAuto { get; set; } = true;
    [Property, Category("Combat")] public override bool HasHipFire { get; set; } = true;
    [Property, Category("Combat")] public override bool CanAttackWithoutAmmo { get; set; } = false;
    [Property, Category("Reload")] public override bool HasReload { get; set; } = true;
    [Property, Category("Reload")] public override float ReloadDurationSeconds { get; set; } = 1f;

    protected override void OnWeaponStart()
    {
        Ammo = ClipSize;
        Log.Info("[WeaponUsp] Ready");
    }
}
