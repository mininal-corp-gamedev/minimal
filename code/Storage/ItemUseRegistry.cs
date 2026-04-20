using Ambi.Storage;

public static class ItemUseRegistry
{
    private static readonly Dictionary<string, IItemUseHandler> _handlers = new();

    public static void Register(string itemId, IItemUseHandler handler)
    {
        _handlers[itemId] = handler;
    }

    public static bool TryUse(Item item, Player caller)
    {
        if (!_handlers.TryGetValue(item.Id, out var handler))
            return false;

        var successful = handler.Use(item, caller);

        return successful;
    }

    public static void TrySwitch(Item item, Player caller)
    {
        if (!_handlers.TryGetValue(item.Id, out var handler))
            return;

        handler.OnSwitched(item, caller);
    }
}