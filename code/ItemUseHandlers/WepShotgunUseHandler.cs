using Ambi.Storage;

using Sandbox;

namespace Minimal.ItemUseHandlers;

public sealed class WepShotgunUseHandler : IItemUseHandler
{
    public bool Use(Item item, Player caller)
    {
        var weapon = WeaponManager.Instance?.Shotgun;
        if (!weapon.IsValid())
            return false;

        caller.SwitchWeapon(weapon);

        return true;
    }
}
