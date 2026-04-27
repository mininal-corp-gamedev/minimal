using System;
using static Sandbox.Clothing;

namespace Ambi.Storage;

public sealed class Inventory
{
    public IReadOnlyList<Slot> Slots => _slots;
    private readonly List<Slot> _slots = new();

    public Action OnChanged;
    public Action<Item, int> OnItemAdded;
    public Action<Item, int> OnItemRemoved;
    public Action<Slot, Slot> OnMoveOrSwapSucceeded;
    public Action<Slot, Slot> OnMoveOrSwapSameSlotAttempted;
    public Action<Slot> OnMoveOrSwapOutsideAttempted;
    public Action<Slot> OnUsed;

    public Inventory(int slotCount)
    {
        SetSlotCount(slotCount);
    }

    public void SetSlotCount(int newCount)
    {
        if (newCount < _slots.Count)
        {
            for (int i = newCount; i < _slots.Count; i++)
            {
                var slot = _slots[i];
                if (!slot.IsEmpty)
                {
                    OnItemRemoved?.Invoke(slot.Item!, slot.Item!.Count);
                }
            }

            _slots.RemoveRange(newCount, _slots.Count - newCount);
        }
        else
        {
            for (int i = _slots.Count; i < newCount; i++)
            {
                _slots.Add(new Slot());
            }
        }

        OnChanged?.Invoke();
    }

    public bool AddItem(Item item)
    {
        int remaining = item.Count;
        int addedTotal = 0;

        // 1. Докидываем в существующие стаки
        foreach (var slot in _slots)
        {
            if (slot.Item == null)
                continue;

            if (slot.Item.Id != item.Id)
                continue;

            if (remaining <= 0)
                break;

            int before = slot.Item.Count;

            slot.Item.Add(remaining);

            int added = slot.Item.Count - before;
            remaining -= added;
            addedTotal += added;
        }

        // 2. Создаём новые стаки
        foreach (var slot in _slots)
        {
            if (remaining <= 0)
                break;

            if (!slot.IsEmpty)
                continue;

            int stack = Math.Min(item.MaxCount, remaining);
            remaining -= stack;

            var newItem = Item.Create(item.Id, stack);
            slot.Set(newItem);

            addedTotal += stack;
        }

        if (addedTotal > 0)
        {
            OnItemAdded?.Invoke(item, addedTotal);
            OnChanged?.Invoke();
        }

        return remaining == 0;
    }

    public int RemoveItem(string id, int count)
    {
        int remaining = count;
        int removedTotal = 0;

        foreach (var slot in _slots)
        {
            if (slot.Item == null)
                continue;

            if (slot.Item.Id != id)
                continue;

            if (remaining <= 0)
                break;

            int before = slot.Item.Count;

            slot.Item.Remove(remaining);

            int removed = before - slot.Item.Count;
            remaining -= removed;
            removedTotal += removed;

            if (slot.Item.Count <= 0)
                slot.Clear();
        }

        if (removedTotal > 0)
        {
            var removedItem = Item.Create(id, removedTotal);
            OnItemRemoved?.Invoke(removedItem, removedTotal);
            OnChanged?.Invoke();
        }

        return removedTotal;
    }

    public int RemoveItem(Slot slot, int count)
    {
        if (slot.Item == null || count <= 0)
            return 0;

        int before = slot.Item.Count;
        slot.Item.Remove(count);
        int removed = before - slot.Item.Count;

        if (removed > 0)
        {
            OnItemRemoved?.Invoke(Item.Create(slot.Item?.Id ?? "", removed), removed);
            OnChanged?.Invoke();
        }

        if (slot.Item.Count <= 0)
            slot.Clear();

        return removed;
    }

    public bool CanAddItem(string id, int amount)
    {
        if (amount <= 0)
            return true;

        var def = ItemDatabase.Get(id);
        if (def is null)
            return false;

        var maxCount = Math.Max(1, def.MaxCount);
        int remaining = amount;

        foreach (var slot in _slots)
        {
            if (slot.Item == null)
                continue;

            if (slot.Item.Id != id)
                continue;

            int freeSpace = maxCount - slot.Item.Count;
            if (freeSpace <= 0)
                continue;

            remaining -= freeSpace;

            if (remaining <= 0)
                return true;
        }

        int emptySlots = 0;
        foreach (var slot in _slots)
        {
            if (slot.IsEmpty)
                emptySlots++;
        }

        int maxFromEmptySlots = emptySlots * maxCount;

        return remaining <= maxFromEmptySlots;
    }

    public int GetTotalCount(string id)
    {
        int total = 0;

        foreach (var slot in Slots)
        {
            if (!slot.IsEmpty && slot.Item.Id == id)
                total += slot.Item.Count;
        }

        return total;
    }

    public bool TryUseItem(Slot slot, Player caller)
    {
        if (slot.Item == null)
            return false;

        var item = slot.Item;

        if (!item.Definition.CanUse)
            return false;

        var successful = ItemUseRegistry.TryUse(item, caller);

        if (item.Count <= 0)
            slot.Clear();

        if (successful)
        {
            OnUsed?.Invoke(slot);
        }

        return successful;
    }

    public bool TrySwitchItem(Slot slot, Player caller)
    {
        if (slot.Item == null)
            return false;

        var item = slot.Item;

        if (!item.Definition.CanUse)
            return false;

        ItemUseRegistry.TrySwitch(item, caller);

        return true;
    }

    public bool TryMoveOrSwap(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= _slots.Count)
        {
            Log.Info($"[Inventory] TryMoveOrSwap failed: invalid fromIndex={fromIndex}, slots={_slots.Count}.");
            return false;
        }

        if (toIndex < 0 || toIndex >= _slots.Count)
        {
            Log.Info($"[Inventory] TryMoveOrSwap failed: invalid toIndex={toIndex}, slots={_slots.Count}.");
            return false;
        }

        var fromSlot = _slots[fromIndex];
        var toSlot = _slots[toIndex];

        if (fromIndex == toIndex)
        {
            Log.Info($"[Inventory] TryMoveOrSwap canceled: same slot {fromIndex}.");
            OnMoveOrSwapSameSlotAttempted?.Invoke(fromSlot, toSlot);
            return false;
        }

        if (fromSlot.IsEmpty)
        {
            Log.Info($"[Inventory] TryMoveOrSwap failed: source slot {fromIndex} is empty.");
            return false;
        }

        if (toSlot.IsEmpty)
        {
            Log.Info($"[Inventory] Move item {fromSlot.Item?.Id} x{fromSlot.Item?.Count} from {fromIndex} to empty slot {toIndex}.");
            toSlot.Set(fromSlot.Item);
            fromSlot.Clear();
        }
        else
        {
            Log.Info($"[Inventory] Swap slot {fromIndex} ({fromSlot.Item?.Id} x{fromSlot.Item?.Count}) with slot {toIndex} ({toSlot.Item?.Id} x{toSlot.Item?.Count}).");
            fromSlot.Switch(toSlot);
        }

        OnChanged?.Invoke();
        OnMoveOrSwapSucceeded?.Invoke(fromSlot, toSlot);
        Log.Info("[Inventory] TryMoveOrSwap success.");
        return true;
    }

    public void NotifyOutsideMoveOrSwapAttempt(int fromIndex)
    {
        if (fromIndex < 0 || fromIndex >= _slots.Count)
            return;

        var fromSlot = _slots[fromIndex];
        OnMoveOrSwapOutsideAttempted?.Invoke(fromSlot);
        Log.Info($"[Inventory] Outside move/swap attempt from slot {fromIndex}.");
    }

    public int? GetIndex(Slot slot)
    {
        for (int i = 0; i < _slots.Count; i++)
            if (_slots[i] == slot)
                return i;

        return null;
    }

    /// <summary>Clears all inventory slots and invokes OnChanged.</summary>
    public void ClearAll()
    {
        foreach (var slot in _slots)
            slot.Clear();
        OnChanged?.Invoke();
    }
}
