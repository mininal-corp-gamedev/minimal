namespace Ambi.Storage;

public sealed class Slot
{
    public Item Item { get; private set; }

    public bool IsEmpty => Item == null;

    public void Set(Item item)
    {
        Item = item;
    }

    public void Clear()
    {
        Item = null;
    }

    public void Switch(Slot other)
    {
        if (other == null || ReferenceEquals(this, other))
            return;

        var temp = Item;
        Item = other.Item;
        other.Item = temp;
    }

    public override string ToString()
        => IsEmpty ? $"Empty" : $"{Item.Definition.Header} x{Item.Count}";
}
