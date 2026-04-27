using Ambi.Storage;
using System;

namespace Minimal.ItemUseHandlers;

public sealed class PizzaUseHandler : IItemUseHandler
{
    private int health = 20;

    public bool Use(Item item, Player caller)
    {
        item.Remove(1);

        caller.Health = Math.Min(caller.Health + health, caller.MaxHealth);

        return true;
    }
}
