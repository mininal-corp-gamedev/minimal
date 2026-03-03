using System;

namespace SilentEcho.Storage;

public sealed class ItemTea : Item
{
    public ItemTea(int count = 1) : base(type: "tea", name: "Tea", desc: "For drinks", count: count)
    {
    }
}