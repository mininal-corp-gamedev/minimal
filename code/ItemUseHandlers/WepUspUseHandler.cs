using Ambi.Storage;

using Sandbox;

namespace Megashot.ItemUseHandlers;

public sealed class WepUspUseHandler : IItemUseHandler
{
    public bool Use(Item item, Player caller)
    {
        var weapon = WeaponManager.Instance?.Usp;
        if (!weapon.IsValid())
            return false;

        caller.SwitchWeapon(weapon);

        return true;
    }
}
