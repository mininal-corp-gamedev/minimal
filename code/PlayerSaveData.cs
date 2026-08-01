using System.Text.Json.Serialization;

public static class PlayerSaveData
{
	public const int InventorySlotCount = 27;
	public const int DefaultStartingMoney = 500;
	// Tutorial equipment is unlocked by Mark. A brand-new account starts with fists only.
	public static readonly string[] DefaultInventoryItemIds = { "hands" };
}

public sealed class PlayerMoneySaveData
{
	[JsonPropertyName( "steamId" )] public long SteamId { get; set; }
	[JsonPropertyName( "money" )] public int Money { get; set; }
	[JsonPropertyName( "moneyAtm" )] public int MoneyAtm { get; set; }
	[JsonPropertyName( "clanId" )] public int? ClanId { get; set; }
	[JsonPropertyName( "clanRank" )] public string ClanRank { get; set; }
}
