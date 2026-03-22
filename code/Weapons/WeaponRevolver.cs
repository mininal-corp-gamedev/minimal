using Sandbox;

namespace Megashot.Weapons;

/// <summary>
/// Револьвер — одиночная стрельба по нажатию, высокий урон, малая обойма.
/// </summary>
public sealed class WeaponRevolver : Weapon
{
    [Property, Category("Ammo")] public override int ClipSize { get; set; } = 6;
    [Property, Category("Ammo")] public override int TotalReserveAmmo { get; set; } = 24;
    [Property, Category("Combat")] public override float FireDelay { get; set; } = 0.5f;
    [Property, Category("Combat")] public override float Damage { get; set; } = 45f;
    [Property, Category("Combat")] public override bool SemiAuto { get; set; } = true;
    [Property, Category("Combat")] public override bool HasHipFire { get; set; } = true;
    [Property, Category("Combat")] public override bool CanAttackWithoutAmmo { get; set; } = false;
    [Property, Category("Reload")] public override bool HasReload { get; set; } = true;
    [Property, Category("Reload")] public override float ReloadDurationSeconds { get; set; } = 2.5f;

    /// <summary>Револьвер: b_reload достаточно вызвать один раз при старте; сброс в false перезапускает анимацию, поэтому при окончании перезарядки ничего не трогаем.</summary>
    protected override void OnReloadFinished()
    {
        // Не ставим b_reload = false и не трогаем viewmodel — у револьвера анимация по одному триггеру.
    }

    protected override void OnWeaponStart()
    {
        Ammo = ClipSize;
        Log.Info("[WeaponRevolver] Ready");
    }
}
