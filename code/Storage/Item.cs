using System;

namespace Ambi.Storage;

public sealed class Item
{
    public string Id { get; }
    public int Count { get; private set; }
    public bool CanDrop { get; set; } = true;
    public bool IsJobItem { get; set; } = false;
    public bool CanSave { get; set; } = true;

    public ItemDefinition Definition => ItemDatabase.Get(Id);

    public Item(string id, int count)
    {
        Id = id;
        Count = count;

        var def = Definition;
        if (def is null)
            return;

        CanDrop = def.CanDrop;
        IsJobItem = def.IsJobItem;
        CanSave = def.CanSave;
    }

    public int MaxCount => Math.Max(1, Definition?.MaxCount ?? 1);
    public bool CanUse => Definition?.CanUse ?? false;

    public void Add(int amount)
    {
        Count = Math.Min(Count + amount, MaxCount);
    }

    public void Remove(int amount)
    {
        Count = Math.Max(Count - amount, 0);
    }

    public bool CanStackWith(Item other)
    {
        return other is not null
            && Id == other.Id
            && CanDrop == other.CanDrop
            && IsJobItem == other.IsJobItem
            && CanSave == other.CanSave;
    }

    public Item CopyWithCount(int count)
    {
        return Create(Id, count, CanDrop, IsJobItem, CanSave);
    }

    public static Item Create(string id, int count, bool? canDrop = null, bool? isJobItem = null, bool? canSave = null)
    {
        var def = ItemDatabase.Get(id);
        var maxCount = Math.Max(1, def?.MaxCount ?? 1);
        var item = new Item(id, Math.Clamp(count, 0, maxCount));

        if (canDrop.HasValue)
            item.CanDrop = canDrop.Value;
        if (isJobItem.HasValue)
            item.IsJobItem = isJobItem.Value;
        if (canSave.HasValue)
            item.CanSave = canSave.Value;

        return item;
    }
}
