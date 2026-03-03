using System;

namespace SilentEcho.Storage;

public class ItemComponent : Component, Component.IPressable
{
    public Item Item { get; set; } = new ItemTea(4);
    [Property, ReadOnly] public string Type { get; private set; } = string.Empty;
    [Property, ReadOnly] public int Count { get; private set; } = 0;

    [Property, Description("Remove game object, if false it'll remove only component")] public bool RemoveAfterTake { get; set; } = true;

    public void Take(Player ply)
    {
        ply.Inventory.Add(Item);
        ply.Hud.Show(5f);

        if (RemoveAfterTake)
            DestroyGameObject();
        else
            Destroy();
    }

    protected override void OnStart()
    {
        if (Item == null) return;

        Type = Item.Type;
        Count = Item.Count;
    }

    public bool Press(IPressable.Event e)
    {
        if (!e.Source.Components.TryGet<Player>(out var ply)) return false;

        Take(ply);

        return true;
    }
}