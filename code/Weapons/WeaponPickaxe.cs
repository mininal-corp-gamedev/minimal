using Sandbox;

namespace Megashot.Weapons;

/// <summary>
/// Кирка / ближний бой: без патронов и перезарядки, удар лучом без снарядов.
/// </summary>
public sealed class WeaponPickaxe : Weapon
{
    [Property, Category("Combat")] public override bool IsMelee { get; set; } = true;
    [Property, Category("Reload")] public override bool HasReload { get; set; } = false;
    [Property, Category("Combat")] public override bool SemiAuto { get; set; } = true;
    [Property, Category("Combat")] public override float FireDelay { get; set; } = 0.45f;
    [Property, Category("Combat")] public override float Damage { get; set; } = 25f;
    [Property, Category("Combat")] public override float AttackRange { get; set; } = 80f;
}
