using Ambi.Storage;

namespace Megashot.ItemUseHandlers;

public sealed class WepMp5UseHandler : IItemUseHandler
{
    public bool Use(Item item, Player caller)
    {
        //if (caller.CurrentWeapon == WeaponManager.Instance.Mp5) return false;

        caller.SwitchWeapon(WeaponManager.Instance.Mp5);

        return true;
    }

    public void OnSwitched(Item item, Player caller)
    {
        //caller.SwitchWeapon();
    }
}
