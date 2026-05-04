using Sandbox;
using Minimal.Shop;

public sealed class ShopObject : Component
{
    [Sync(SyncFlags.FromHost)] public Player PlayerOwner { get; set; }
    [Sync(SyncFlags.FromHost)] public ShopDefinition Definition { get; set; }
}