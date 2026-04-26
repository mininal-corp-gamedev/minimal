using Ambi.Storage;

using Sandbox;

namespace Megashot.ItemUseHandlers;

public sealed class WepMp5UseHandler : IItemUseHandler
{
    public bool Use(Item item, Player caller)
    {
        //if (caller.CurrentWeapon == WeaponManager.Instance.Mp5) return false;

        var weapon = WeaponManager.Instance?.Mp5;
        if (!weapon.IsValid())
            return false;

        caller.SwitchWeapon(weapon);

        return true;
    }

    public void OnSwitched(Item item, Player caller)
    {
        //caller.SwitchWeapon();
    }
}
