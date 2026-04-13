using System;
public static class JobDatabase
{
    private static Dictionary<string, JobDefinition> _cache;

    public static JobDefinition Get(string id)
    {
        EnsureLoaded();

        if (!_cache!.TryGetValue(id, out var def))
        {
            Log.Error($"Job Definition '{id}' not found");

            return null;
        }

        return def;
    }

    private static void EnsureLoaded()
    {
        if (_cache != null)
            return;

        _cache = ResourceLibrary
            .GetAll<JobDefinition>()
            .ToDictionary(x => x.Id, x => x);
    }
}