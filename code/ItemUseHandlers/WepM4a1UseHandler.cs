using Ambi.Storage;

using Sandbox;

namespace Minimal.ItemUseHandlers;

public sealed class WepM4a1UseHandler : IItemUseHandler
{
    public bool Use(Item item, Player caller)
    {
        var weapon = WeaponManager.Instance?.M4A1;
        if (!weapon.IsValid())
            return false;

        caller.SwitchWeapon(weapon);

        return true;
    }
}
