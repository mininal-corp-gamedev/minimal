using Ambi.Storage;

namespace Megashot.ItemUseHandlers;

public sealed class WepUspUseHandler : IItemUseHandler
{
    public bool Use(Item item, Player caller)
    {
        caller.SwitchWeapon(WeaponManager.Instance.Usp);

        return true;
    }
}
