using Sandbox.UI;
using System;
using System.Threading;

namespace SilentEcho.Storage;

public abstract class Item
{
    public string Type { get; set; } = "";
    public int Count { get; set; } = 1;
    public string Name { get; set; } = "Default";
    public string Description { get; set; } = "Default";

    protected Item(string type, string name, string desc, int count)
    {
        Type = type;
        Name = name;
        Description = desc;
        Count = count;
    }

    public static bool Compare(Item item1, Item item2)
    {
        if (item1 == null || item2 == null) return false;
        if (item1.Type != item2.Type) return false;

        return true;
    }
}
