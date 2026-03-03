using System;

namespace SilentEcho.Storage;

public class Slot
{
    public int Id { get; private set; } = 0;
    public Item Item { get; private set; }

    public Slot(int id)
    {
        Id = id;
    }

    public void Add(Item item)
    {
        if (Item.Compare(item, Item))
            Item.Count += item.Count;
        else
            Item = item;
    }

    public void Remove()
    {

    }

    public bool IsEmpty => Item == null;

    public override string ToString()
    {
        var name = Item == null ? "Empty" : Item.Name + " x" + Item.Count;

        return $"{Id}. {name}";
    }
}
