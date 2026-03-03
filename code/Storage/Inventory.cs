using Sandbox;

namespace SilentEcho.Storage;

public sealed class Inventory : Component
{
    [Property] public int MaxSlots { get; set; } = 10;
    [Property, ReadOnly] public List<Slot> Slots { get; set; } = new List<Slot>();

    public void Add(Item item)
    {
        //! a just simple realistion
        foreach (Slot slot in Slots)
        {
            if (!slot.IsEmpty && !Item.Compare(slot.Item, item)) continue;

            slot.Add(item);

            break;
        }
    }

    private void Init()
    {
        Slots.Capacity = MaxSlots;

        for (int i = 0; i < MaxSlots; i++)
            Slots.Add(new Slot(i));
    }

    protected override void OnStart()
    {
        Init();
    }
}
