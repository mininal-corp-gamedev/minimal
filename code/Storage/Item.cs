using System;

namespace Ambi.Storage;

public sealed class Item
{
    public string Id { get; }
    public int Count { get; private set; }
    public bool CanDrop { get; set; } = true;
    public bool CanSave { get; set; } = true;

    public ItemDefinition Definition => ItemDatabase.Get(Id);

    public Item(string id, int count)
    {
        Id = id;
        Count = count;
    }

    public int MaxCount => Definition.MaxCount;
    public bool CanUse => Definition.CanUse;

    public void Add(int amount)
    {
        Count = Math.Min(Count + amount, MaxCount);
    }

    public void Remove(int amount)
    {
        Count = Math.Max(Count - amount, 0);
    }

    public static Item Create(string id, int count)
    {
        var def = ItemDatabase.Get(id);
        return new Item(id, count = Math.Min(count, def.MaxCount));
    }
}