using Sandbox;
using System;

namespace Ambi.Storage;

public sealed class ItemComponent : Component, Component.ICollisionListener, Component.IPressable
{
    [Property] public ItemDefinition ItemDefinition { get; set; }
    [Property] public ModelRenderer Model { get; set; }
    [Property] public int Count { get; set; } = 1;
    [Property] public SoundEvent PickUpSound { get; set; }
    [Property, Group("Inventory")] public bool CanDrop { get; set; } = true;
    [Property, Group("Inventory")] public bool IsJobItem { get; set; } = false;
    [Property, Group("Inventory")] public bool CanSave { get; set; } = true;
    [Property, Group("Pickup")] public float PickupRadius { get; set; } = 140f;
    [Property, Group("Pickup")] public bool UsePickupMagnet { get; set; } = true;

    [Property, Group("Auto Destroy")]
    public bool DelayDeleteSpawn { get; set; }

    [Property, Group("Auto Destroy")]
    public float DelayDestroy { get; set; } = 30f;

    private TimeUntil _delayDestroy;

    protected override void OnStart()  
    {
        if (DelayDeleteSpawn && DelayDestroy > 0f)
            _delayDestroy = DelayDestroy;
    }

    protected override void OnUpdate()
    {
        if (DelayDeleteSpawn && _delayDestroy)
        {
            GameObject.Destroy();
        }
    }

    /// <summary>
    /// Попытка забрать предмет в инвентарь
    /// </summary>
    public bool RequestPickup(Player player)
    {
        if (!player.IsValid())
            return false;

        if (Networking.IsHost)
            return player.HostTryPickup(this) > 0;

        RpcRequestPickup(GameObject);
        return true;
    }

    public Item CreateItem(int count)
    {
        if (ItemDefinition is null)
            return null;

        return Item.Create(ItemDefinition.Id, count, CanDrop, IsJobItem, CanSave);
    }

    public int TryPickup(Inventory inventory)
    {
        if (inventory is null || ItemDefinition is null)
            return 0;

        if (Count <= 0)
        {
            GameObject.Destroy();
            return 0;
        }

        var def = ItemDatabase.Get(ItemDefinition.Id);
        if (def is null)
            return 0;

        var maxStack = Math.Max(1, def.MaxCount);

        int totalTaken = 0;

        while (Count > 0)
        {
            // Сколько мы пытаемся положить за раз (не больше стака)
            int tryTake = Math.Min(maxStack, Count);

            var item = CreateItem(tryTake);
            if (item is null)
                break;

            // Проверяем, влезет ли хотя бы столько
            if (!inventory.CanAddItem(item))
                break;

            bool fullyAdded = inventory.AddItem(item);

            if (!fullyAdded)
                break;

            Count -= tryTake;
            totalTaken += tryTake;
        }

        if (totalTaken > 0)
        {
            var itemName = string.IsNullOrWhiteSpace(ItemDefinition.Header) ? ItemDefinition.Id : ItemDefinition.Header;

            // Если всё отдали — уничтожаем объект
            if (Count <= 0)
            {
                Sound.Play(PickUpSound, WorldPosition);

                GameObject.Destroy();
            }
            // Иначе объект остаётся с остатком
        }

        return totalTaken;
    }

    void ICollisionListener.OnCollisionStart(Collision collision)
    {
        if (!collision.Other.GameObject.Components.TryGet<Player>(out var ply, FindMode.EverythingInSelfAndAncestors)) return;

        RequestPickup(ply);
    }

    public bool Press(IPressable.Event e)
    {
        var source = e.Source?.GameObject;
        if (!source.IsValid())
            return false;

        if (!source.Components.TryGet<Player>(out var ply, FindMode.EverythingInSelfAndParent))
            return false;

        if (ply.IsProxy)
            return false;

        return RequestPickup(ply);
    }

    [Rpc.Host]
    private static void RpcRequestPickup(GameObject itemObject)
    {
        if (!Networking.IsHost)
            return;
        if (!itemObject.IsValid())
            return;

        var caller = Rpc.Caller;
        if (caller is null)
            return;

        var player = Player.FindPlayerBySteamId(caller.SteamId.Value);
        if (!player.IsValid())
            return;

        var item = itemObject.Components.Get<ItemComponent>();
        if (!item.IsValid())
            return;

        player.HostTryPickup(item);
    }
}
