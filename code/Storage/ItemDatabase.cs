using System;

namespace Ambi.Storage;

public static class ItemDatabase
{
    private static Dictionary<string, ItemDefinition> _cache;

    public static ItemDefinition Get(string id)
    {
        EnsureLoaded();

        if (!_cache!.TryGetValue(id, out var def))
        {
            Log.Error($"ItemDefinition '{id}' not found");

            return null;
        }

        return def;
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