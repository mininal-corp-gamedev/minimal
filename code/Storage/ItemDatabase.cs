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
        var normalized = (id ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            definition = null;
            return false;
        }

        return _cache!.TryGetValue(normalized, out definition);
    }

    private static void EnsureLoaded()
    {
        if (_cache != null)
            return;

        _cache = new Dictionary<string, ItemDefinition>();

        foreach (var item in ResourceLibrary.GetAll<ItemDefinition>())
        {
            var id = (item.Id ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(id))
            {
                Log.Warning($"ItemDatabase: item '{item.Header}' has empty Id.");
                continue;
            }

            if (_cache.TryGetValue(id, out var existing))
            {
                Log.Warning($"ItemDatabase: duplicate item id '{id}' ({existing.Header} vs {item.Header}).");
                continue;
            }

            _cache[id] = item;
        }
    }
}
