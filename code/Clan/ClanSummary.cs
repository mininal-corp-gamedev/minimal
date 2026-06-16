using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Minimal.Clan;

public sealed class ClanSummary
{
	[JsonPropertyName( "id" )] public int Id { get; set; }
	[JsonPropertyName( "header" )] public string Header { get; set; } = "";
	[JsonPropertyName( "colorId" )] public string ColorId { get; set; } = ClanPalette.DefaultColorId;
	[JsonPropertyName( "description" )] public string Description { get; set; } = "";
	[JsonPropertyName( "logo" )] public ClanLogo Logo { get; set; } = new();
	[JsonPropertyName( "balance" )] public int Balance { get; set; }
	[JsonPropertyName( "memberCount" )] public int MemberCount { get; set; }
	[JsonPropertyName( "kills" )] public int Kills { get; set; }
	[JsonPropertyName( "leaderLastSeenRaw" )] public string LeaderLastSeenRaw { get; set; } = "";
	[JsonPropertyName( "leaderSteamId" )] public long LeaderSteamId { get; set; }
	[JsonPropertyName( "leaderOnline" )] public bool LeaderOnline { get; set; }
	[JsonPropertyName( "onlineMembers" )] public List<ClanMemberSummary> OnlineMembers { get; set; } = new();
}

public sealed class ClanMemberSummary
{
	[JsonPropertyName( "steamId" )] public long SteamId { get; set; }
	[JsonPropertyName( "name" )] public string Name { get; set; } = "";
	[JsonPropertyName( "rank" )] public ClanRank Rank { get; set; } = ClanRank.Soldier;
}
