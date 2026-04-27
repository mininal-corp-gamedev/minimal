using Sandbox;

namespace Minimal.Weapons;

/// <summary>
/// M4A1 — штурмовая винтовка, стрельба очередью, больший урон и дальность.
/// </summary>
public sealed class WeaponM4a1 : Weapon
{
    [Property, Category("Ammo")] public override int ClipSize { get; set; } = 30;
    [Property, Category("Ammo")] public override int TotalReserveAmmo { get; set; } = 90;
    [Property, Category("Combat")] public override float FireDelay { get; set; } = 0.09f;
    [Property, Category("Combat")] public override float Damage { get; set; } = 12f;
    [Property, Category("Combat")] public override bool SemiAuto { get; set; } = false;
    [Property, Category("Combat")] public override bool HasHipFire { get; set; } = true;
    [Property, Category("Combat")] public override bool CanAttackWithoutAmmo { get; set; } = false;
    [Property, Category("Reload")] public override bool HasReload { get; set; } = true;
    [Property, Category("Reload")] public override float ReloadDurationSeconds { get; set; } = 2.2f;

    /// <summary>У M4A1 b_reload как у револьвера — смена значения перезапускает анимацию, поэтому по окончании перезарядки ничего не ставим.</summary>
    protected override void OnReloadFinished()
    {
    }

    protected override void OnWeaponStart()
    {
        Ammo = ClipSize;
        Log.Info("[WeaponM4a1] Ready");
    }
}
