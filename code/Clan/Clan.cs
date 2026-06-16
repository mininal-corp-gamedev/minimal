using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Minimal.Clan;

public sealed class Clan
{
	[JsonPropertyName( "id" )] public int Id { get; set; }
	[JsonPropertyName( "header" )] public string Header { get; set; } = "";
	[JsonPropertyName( "colorId" )] public string ColorId { get; set; } = ClanPalette.DefaultColorId;
	[JsonPropertyName( "description" )] public string Description { get; set; } = "";
	[JsonPropertyName( "logo" )] public ClanLogo Logo { get; set; } = new();
	[JsonPropertyName( "balance" )] public int Balance { get; set; }
	[JsonPropertyName( "memberSteamIds" )] public List<long> MemberSteamIds { get; set; } = new();
	[JsonPropertyName( "memberRanks" )] public Dictionary<long, ClanRank> MemberRanks { get; set; } = new();
	[JsonPropertyName( "kills" )] public int Kills { get; set; }
	[JsonPropertyName( "leaderLastSeenRaw" )] public string LeaderLastSeenRaw { get; set; } = "";
	[JsonPropertyName( "leaderSteamId" )] public long LeaderSteamId { get; set; }

	[JsonIgnore] public int MemberCount => MemberSteamIds?.Count ?? 0;

	public ClanRank GetRank( long steamId )
	{
		if ( steamId == LeaderSteamId )
			return ClanRank.Leader;

		return MemberRanks is not null && MemberRanks.TryGetValue( steamId, out var rank )
			? rank
			: ClanRank.Soldier;
	}

	public void SetRank( long steamId, ClanRank rank )
	{
		MemberRanks ??= new Dictionary<long, ClanRank>();
		MemberRanks[steamId] = steamId == LeaderSteamId ? ClanRank.Leader : rank;
	}

	public void Normalize()
	{
		Header = ClanText.NormalizeHeader( Header );
		Description = ClanText.NormalizeDescription( Description );
		ColorId = ClanPalette.NormalizeColorId( ColorId );
		Logo ??= new ClanLogo();
		Logo.Normalize();
		MemberSteamIds ??= new List<long>();
		MemberRanks ??= new Dictionary<long, ClanRank>();
		MemberSteamIds.RemoveAll( x => x == 0L );

		if ( LeaderSteamId != 0L && !MemberSteamIds.Contains( LeaderSteamId ) )
			MemberSteamIds.Insert( 0, LeaderSteamId );

		foreach ( var steamId in MemberSteamIds )
		{
			if ( !MemberRanks.ContainsKey( steamId ) )
				MemberRanks[steamId] = steamId == LeaderSteamId ? ClanRank.Leader : ClanRank.Soldier;
		}

		var knownMembers = new HashSet<long>( MemberSteamIds );
		foreach ( var steamId in new List<long>( MemberRanks.Keys ) )
		{
			if ( !knownMembers.Contains( steamId ) )
				MemberRanks.Remove( steamId );
		}

		if ( LeaderSteamId != 0L )
			MemberRanks[LeaderSteamId] = ClanRank.Leader;
	}
}
