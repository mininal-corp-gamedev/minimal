using System;
using System.Text.Json;

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

    public void EnsureMinimumSlotCount(int minimum)
    {
        if (minimum > _slots.Count)
            SetSlotCount(minimum);
    }

    public bool AddItem(Item item)
    {
        if (item is null || item.Count <= 0)
            return false;

        // AddItem is an all-or-nothing operation. Callers must never receive
        // false after part of an item stack has already been inserted.
        if (!CanAddItem(item))
            return false;

        int remaining = item.Count;
        int addedTotal = 0;

        // 1. Докидываем в существующие стаки
        foreach (var slot in _slots)
        {
            if (slot.Item == null)
                continue;

            if (!slot.Item.CanStackWith(item))
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

            var newItem = item.CopyWithCount(stack);
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
        return CanAddItem(Item.Create(id, amount));
    }

    public bool CanAddItem(Item item)
    {
        if (item is null)
            return false;
        if (item.Count <= 0)
            return true;

        var def = item.Definition;
        if (def is null)
            return false;

        var maxCount = Math.Max(1, def.MaxCount);
        int remaining = item.Count;

        foreach (var slot in _slots)
        {
            if (slot.Item == null)
                continue;

            if (!slot.Item.CanStackWith(item))
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
        if (slot?.Item == null || caller is null)
            return false;

        var item = slot.Item;

        var beforeCount = item.Count;
        var definition = item.Definition;
        if (definition is null || !definition.CanUse)
            return false;

        bool successful;
        try
        {
            successful = ItemUseRegistry.TryUse(item, caller);
        }
        catch (Exception ex)
        {
            item.RestoreCount(beforeCount);
            Log.Error($"[Inventory] Item handler for '{item.Id}' failed: {ex.Message}");
            return false;
        }

        if (!successful)
        {
            // A failed handler is not allowed to consume or add inventory items.
            item.RestoreCount(beforeCount);
            return false;
        }

        var removed = Math.Max(0, beforeCount - item.Count);
        var countChanged = item.Count != beforeCount;
        if (item.Count <= 0)
            slot.Clear();

        if (removed > 0)
            OnItemRemoved?.Invoke(item.CopyWithCount(removed), removed);

        OnUsed?.Invoke(slot);

        if (countChanged)
            OnChanged?.Invoke();

        return true;
    }

    public bool TrySwitchItem(Slot slot, Player caller)
    {
        if (slot.Item == null)
            return false;

        var item = slot.Item;

        if (item.Definition is null || !item.Definition.CanUse)
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

    public int RemoveJobItems()
    {
        int removedTotal = 0;

        foreach (var slot in _slots)
        {
            if (slot.Item is null || !slot.Item.IsJobItem)
                continue;

            removedTotal += slot.Item.Count;
            OnItemRemoved?.Invoke(slot.Item, slot.Item.Count);
            slot.Clear();
        }

        if (removedTotal > 0)
            OnChanged?.Invoke();

        return removedTotal;
    }

    public InventorySnapshot CreateSnapshot(long steamId = 0, bool includeNonSaveItems = true)
    {
        var snapshot = new InventorySnapshot
        {
            SteamId = steamId,
            SlotCount = _slots.Count
        };

        for (int i = 0; i < _slots.Count; i++)
        {
            var item = _slots[i].Item;
            if (item is null)
                continue;
            if (!includeNonSaveItems && !item.CanSave)
                continue;

            snapshot.Slots.Add(new InventorySlotSnapshot
            {
                Index = i,
                Id = item.Id,
                Count = item.Count,
                CanDrop = item.CanDrop,
                IsJobItem = item.IsJobItem,
                CanSave = item.CanSave
            });
        }

        return snapshot;
    }

    public string CreateSnapshotJson(long steamId = 0, bool includeNonSaveItems = true)
    {
        return JsonSerializer.Serialize(CreateSnapshot(steamId, includeNonSaveItems));
    }

    public bool ApplySnapshotJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return false;

        try
        {
            var snapshot = JsonSerializer.Deserialize<InventorySnapshot>(json);
            if (snapshot is null)
                return false;

            ApplySnapshot(snapshot);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning($"[Inventory] Failed to apply snapshot: {ex.Message}");
            return false;
        }
    }

    public void ApplySnapshot(InventorySnapshot snapshot)
    {
        if (snapshot is null)
            return;

        var slotCount = snapshot.SlotCount > 0 ? snapshot.SlotCount : _slots.Count;
        if (slotCount < _slots.Count)
            _slots.RemoveRange(slotCount, _slots.Count - slotCount);
        else
        {
            for (int i = _slots.Count; i < slotCount; i++)
                _slots.Add(new Slot());
        }

        foreach (var slot in _slots)
            slot.Clear();

        foreach (var savedSlot in snapshot.Slots)
        {
            if (savedSlot is null)
                continue;
            if (savedSlot.Index < 0 || savedSlot.Index >= _slots.Count)
                continue;
            if (string.IsNullOrWhiteSpace(savedSlot.Id) || savedSlot.Count <= 0)
                continue;
            if (!ItemDatabase.TryGet(savedSlot.Id, out _))
                continue;

            var item = Item.Create(savedSlot.Id, savedSlot.Count, savedSlot.CanDrop, savedSlot.IsJobItem, savedSlot.CanSave);
            _slots[savedSlot.Index].Set(item);
        }

        OnChanged?.Invoke();
    }
}
