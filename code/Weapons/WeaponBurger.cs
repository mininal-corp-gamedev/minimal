using Ambi.Storage;
using Sandbox;

namespace Minimal.Weapons;

/// <summary>
/// Все важные проверки делает хост (server authority).
/// </summary>
public sealed class WeaponBurger : Weapon
{
    [Property, Category("Combat")] public override bool IsMelee { get; set; } = true;
    [Property, Category("Reload")] public override bool HasReload { get; set; } = false;
    [Property, Category("Combat")] public override bool SemiAuto { get; set; } = true;
    [Property, Category("Combat")] public override float FireDelay { get; set; } = 0.45f;
    [Property, Category("Combat")] public override float Damage { get; set; } = 0f;
    [Property, Category("Combat")] public override float AttackRange { get; set; } = 90f;

    [Property, Category("Eat")] public int Health { get; set; } = 10;

    public Item Item { get; set; }

    protected override void PerformFire() //
    {
        if (!_timeUntilNextFire) return;

        _firedThisFrame = true;
        _timeUntilNextFire = FireDelay;

        Eat(Player.Local);
    }

    [Rpc.Host]
    private void Eat(Player player)
    {
#if SERVER
        if (!Networking.IsHost)
            return;
        if (Rpc.Caller != player.Network.Owner)
        {
            Rpc.Caller.Kick("[Weapon Burger] try to food the non-owned player");

            return;
        }

        var itemId = player.CurrentWeaponItemId;
        if (string.IsNullOrWhiteSpace(itemId)) return;

        player.Health = MathF.Min(player.Health + Health, player.MaxHealth);
        player.WorldHud?.WorldHudRefresh();
        player.Inventory.RemoveItem(itemId, 1);
#endif
    }
}
