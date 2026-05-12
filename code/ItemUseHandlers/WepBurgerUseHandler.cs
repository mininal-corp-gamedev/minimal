using Ambi.Storage;
using Minimal.Weapons;
using Sandbox;

namespace Minimal.ItemUseHandlers;

public sealed class WepBurgerUseHandler : IItemUseHandler
{
    public bool Use(Item item, Player caller)
    {
        var weapon = WeaponManager.Instance?.Burger;
        if (!weapon.IsValid())
            return false;

        caller.SwitchWeapon(weapon);
        weapon.Components.Get<WeaponBurger>().Item = item;

        return true;
    }
}
