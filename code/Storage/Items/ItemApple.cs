using System;

namespace SilentEcho.Storage;

public sealed class ItemApple : Item
{
    public ItemApple(int count = 1) : base(type: "apple", name: "Apple", desc: "Simple meal", count: count)
    {
    }
}