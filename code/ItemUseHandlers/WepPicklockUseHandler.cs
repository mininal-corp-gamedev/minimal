using Ambi.Storage;

using Sandbox;

namespace Minimal.ItemUseHandlers;

public sealed class WepPicklockUseHandler : IItemUseHandler
{
    public bool Use(Item item, Player caller)
    {
        var weapon = WeaponManager.Instance?.Picklock;
        if (!weapon.IsValid())
            return false;

        caller.SwitchWeapon(weapon);

        return true;
    }
}
