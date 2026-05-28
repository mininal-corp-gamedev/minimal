using Ambi.Storage;
using Sandbox;
using System;

namespace Minimal.ItemUseHandlers;

public sealed class ArmorUseHandler : IItemUseHandler
{
    private const float ArmorAmount = 100f;

    public bool Use(Item item, Player caller)
    {
        if (!caller.IsValid() || item is null)
            return false;

        var maxArmor = MathF.Max(0f, caller.MaxArmor);
        if (maxArmor <= 0f || caller.Armor >= maxArmor)
            return false;

        var newArmor = Math.Clamp(caller.Armor + ArmorAmount, 0f, maxArmor);
        if (Networking.IsHost)
            caller.HostSetArmor(newArmor);
        else
            caller.Armor = newArmor;

        item.Remove(1);
        return true;
    }
}
