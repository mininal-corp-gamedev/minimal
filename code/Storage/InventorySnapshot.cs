using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Ambi.Storage;

public sealed class InventorySnapshot
{
    [JsonPropertyName("steamId")] public long SteamId { get; set; }
    [JsonPropertyName("slotCount")] public int SlotCount { get; set; }
    [JsonPropertyName("slots")] public List<InventorySlotSnapshot> Slots { get; set; } = new();
}

public sealed class InventorySlotSnapshot
{
    [JsonPropertyName("index")] public int Index { get; set; }
    [JsonPropertyName("id")] public string Id { get; set; }
    [JsonPropertyName("count")] public int Count { get; set; }
    [JsonPropertyName("canDrop")] public bool CanDrop { get; set; } = true;
    [JsonPropertyName("isJobItem")] public bool IsJobItem { get; set; }
    [JsonPropertyName("canSave")] public bool CanSave { get; set; } = true;
}
