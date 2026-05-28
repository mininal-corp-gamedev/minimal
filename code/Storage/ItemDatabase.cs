using System;

namespace Ambi.Storage;

public static class ItemDatabase
{
    private static Dictionary<string, ItemDefinition> _cache;

    public static ItemDefinition Get(string id)
    {
        EnsureLoaded();

        if (!TryGet(id, out var def))
        {
            Log.Error($"ItemDefinition '{id}' not found");

            return null;
        }

        return def;
    }

    public static bool TryGet(string id, out ItemDefinition definition)
    {
        EnsureLoaded();
        if (string.IsNullOrWhiteSpace(id))
        {
            definition = null;
            return false;
        }

        return _cache!.TryGetValue(id, out definition);
    }

    private static void EnsureLoaded()
    {
        if (_cache != null)
            return;

        _cache = ResourceLibrary
            .GetAll<ItemDefinition>()
            .ToDictionary(x => x.Id, x => x);
    }
}
