using Ambi.Storage;

namespace Megashot.ItemUseHandlers;

public sealed class WepM4a1UseHandler : IItemUseHandler
{
    public bool Use(Item item, Player caller)
    {
        caller.SwitchWeapon(WeaponManager.Instance.M4A1);

        return true;
    }
}
