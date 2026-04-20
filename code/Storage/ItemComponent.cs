using Sandbox;
using System;

namespace Ambi.Storage;

public sealed class ItemComponent : Component, Component.ICollisionListener
{
    [Property] public ItemDefinition ItemDefinition { get; set; }
    [Property] public ModelRenderer Model { get; set; }
    [Property] public int Count { get; set; } = 1;
    [Property] public SoundEvent PickUpSound { get; set; }
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

        int totalTaken = 0;

        while (Count > 0)
        {
            // Сколько мы пытаемся положить за раз (не больше стака)
            int tryTake = Math.Min(def.MaxCount, Count);

            // Проверяем, влезет ли хотя бы столько
            if (!inventory.CanAddItem(ItemDefinition.Id, tryTake))
                break;

            var item = Item.Create(ItemDefinition.Id, tryTake);

            bool fullyAdded = inventory.AddItem(item);

            if (!fullyAdded)
                break;

            Count -= tryTake;
            totalTaken += tryTake;
        }

        if (totalTaken > 0)
        {
            var itemName = string.IsNullOrWhiteSpace(ItemDefinition.Header) ? ItemDefinition.Id : ItemDefinition.Header;
           // Notification.Make($"Picked up {itemName} x{totalTaken}");

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

    private int CalculateTakeAmount(Inventory inventory)
    {
        int maxTry = Count;

        // Быстрая оптимизация: если всё влезает — берём всё
        if (inventory.CanAddItem(ItemDefinition.Id, maxTry))
            return maxTry;

        // Иначе подбираем максимум (редко вызывается)
        for (int i = maxTry; i > 0; i--)
        {
            if (inventory.CanAddItem(ItemDefinition.Id, i))
                return i;
        }

        return 0;
    }

    void ICollisionListener.OnCollisionStart(Collision collision)
    {
        if (!collision.Other.GameObject.Components.TryGet<Player>(out var ply, FindMode.EverythingInSelfAndAncestors)) return;

        //TryPickup(ply.Inventory);
    }
}