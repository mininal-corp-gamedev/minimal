using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Sandbox;

namespace Minimal.Clan;

public sealed partial class ClanManager
{
	private readonly List<Clan> _clans = new();
	private readonly Dictionary<long, PendingClanInvite> _pendingInvites = new();
	private readonly Dictionary<long, TimeUntil> _inviteCooldowns = new();
	private TimeUntil _nextRuntimeSync = 0f;

	private void HostLoadClans()
	{
		if ( !Networking.IsHost )
			return;

		_clans.Clear();
		_clans.AddRange( ClanDatabase.Load() );
		HostPublishSummaries();
	}

	private void HostUpdateRuntimeSync()
	{
		if ( !Networking.IsHost )
			return;
		if ( (float)_nextRuntimeSync > 0f )
			return;

		_nextRuntimeSync = 1f;
		HostPublishSummaries();
	}

	private void HostCreateClan( string header, string description, string colorId, string iconPath, string backgroundType, string backgroundColor1, string backgroundColor2, string backgroundColor3, string backgroundColor4 )
	{
		var caller = Rpc.Caller;
		var player = GetCallerPlayer( caller );
		if ( !player.IsValid() )
		{
			Notify( caller, false, "Your player is not ready." );
			return;
		}

		if ( player.ClanId >= 0 )
		{
			Notify( caller, false, "You are already in a clan." );
			return;
		}

		var normalizedHeader = ClanText.NormalizeHeader( header );
		if ( string.IsNullOrWhiteSpace( normalizedHeader ) )
		{
			Notify( caller, false, "Clan header is empty." );
			return;
		}

		if ( _clans.Any( x => string.Equals( x.Header, normalizedHeader, StringComparison.OrdinalIgnoreCase ) ) )
		{
			Notify( caller, false, "A clan with this header already exists." );
			return;
		}

		if ( player.Money < CreatePrice )
		{
			Notify( caller, false, $"Need ${CreatePrice} to create a clan." );
			return;
		}

		var steamId = caller.SteamId.Value;
		var clan = new Clan
		{
			Id = GetNextClanId(),
			Header = normalizedHeader,
			Description = ClanText.NormalizeDescription( description ),
			ColorId = ClanPalette.NormalizeColorId( colorId ),
			Logo = BuildLogo( iconPath, backgroundType, backgroundColor1, backgroundColor2, backgroundColor3, backgroundColor4 ),
			Balance = 0,
			LeaderSteamId = steamId,
			LeaderLastSeenRaw = DateTimeOffset.UtcNow.ToString( "O" )
		};
		clan.MemberSteamIds.Add( steamId );
		clan.SetRank( steamId, ClanRank.Leader );
		clan.Normalize();

		player.Money -= CreatePrice;
		_clans.Add( clan );
		ApplyClanToPlayer( player, clan, ClanRank.Leader );
		_pendingInvites.Remove( steamId );
		SaveAll();
		Notify( caller, true, $"Clan created: {clan.Header}." );
	}

	private void HostUpdateClanSettings( string description, string colorId, string iconPath, string backgroundType, string backgroundColor1, string backgroundColor2, string backgroundColor3, string backgroundColor4 )
	{
		var caller = Rpc.Caller;
		var player = GetCallerPlayer( caller );
		var clan = GetClanForPlayer( player );
		if ( clan is null )
		{
			Notify( caller, false, "You are not in a clan." );
			return;
		}

		var rank = clan.GetRank( caller.SteamId.Value );
		if ( !CanEditSettingsRank( rank ) )
		{
			Notify( caller, false, "You cannot edit clan settings." );
			return;
		}

		clan.Description = ClanText.NormalizeDescription( description );
		clan.ColorId = ClanPalette.NormalizeColorId( colorId );
		clan.Logo = BuildLogo( iconPath, backgroundType, backgroundColor1, backgroundColor2, backgroundColor3, backgroundColor4 );
		clan.Normalize();
		ApplyClanToOnlineMembers( clan );
		SaveAll();
	}

	private void HostDeleteOwnClan()
	{
		var caller = Rpc.Caller;
		var player = GetCallerPlayer( caller );
		var clan = GetClanForPlayer( player );
		if ( clan is null )
		{
			Notify( caller, false, "You are not in a clan." );
			return;
		}

		var rank = clan.GetRank( caller.SteamId.Value );
		if ( rank != ClanRank.Leader && player.AdminRank < AdminDeleteRank )
		{
			Notify( caller, false, "Only the leader or an admin can delete this clan." );
			return;
		}

		DeleteClan( clan, "Clan deleted." );
	}

	private void HostAdminDeleteClan( int clanId )
	{
		var caller = Rpc.Caller;
		var player = GetCallerPlayer( caller );
		if ( !player.IsValid() || player.AdminRank < AdminDeleteRank )
		{
			Notify( caller, false, "You cannot delete clans as admin." );
			return;
		}

		var clan = FindClan( clanId );
		if ( clan is null )
		{
			Notify( caller, false, "Clan not found." );
			return;
		}

		DeleteClan( clan, "Clan deleted by admin." );
		Notify( caller, true, "Clan deleted by admin." );
	}

	private void HostLeaveClan()
	{
		var caller = Rpc.Caller;
		var player = GetCallerPlayer( caller );
		var clan = GetClanForPlayer( player );
		if ( clan is null )
		{
			Notify( caller, false, "You are not in a clan." );
			return;
		}

		if ( clan.LeaderSteamId == caller.SteamId.Value )
		{
			DeleteClan( clan, "Leader left. Clan deleted." );
			return;
		}

		RemoveMember( clan, caller.SteamId.Value );
		ClearClanFromPlayer( player );
		SaveAll();
		Notify( caller, true, "You left the clan." );
	}

	private void HostInvite( long targetSteamId )
	{
		var caller = Rpc.Caller;
		var inviter = GetCallerPlayer( caller );
		var clan = GetClanForPlayer( inviter );
		if ( clan is null )
		{
			Notify( caller, false, "You are not in a clan." );
			return;
		}

		if ( !CanInviteRank( clan.GetRank( caller.SteamId.Value ) ) )
		{
			Notify( caller, false, "You cannot invite to this clan." );
			return;
		}

		if ( _inviteCooldowns.TryGetValue( caller.SteamId.Value, out var cooldown ) && (float)cooldown > 0f )
		{
			Notify( caller, false, "Clan invite is on cooldown." );
			return;
		}

		var target = Player.FindPlayerBySteamId( targetSteamId );
		if ( !target.IsValid() || target.GameObject.Network.Owner is null )
		{
			Notify( caller, false, "Target player is not online." );
			return;
		}

		if ( targetSteamId == caller.SteamId.Value )
		{
			Notify( caller, false, "You cannot invite yourself." );
			return;
		}

		if ( target.ClanId >= 0 )
		{
			Notify( caller, false, "Target player is already in a clan." );
			return;
		}

		_inviteCooldowns[caller.SteamId.Value] = InviteCooldownSeconds;
		_pendingInvites[targetSteamId] = new PendingClanInvite
		{
			ClanId = clan.Id,
			InviterSteamId = caller.SteamId.Value,
			InviterName = caller.DisplayName ?? ""
		};

		using ( Rpc.FilterInclude( c => c.SteamId.Value == targetSteamId ) )
		{
			RpcReceiveInvite( clan.Id, clan.Header, caller.SteamId.Value, caller.DisplayName ?? "" );
		}

		Notify( caller, true, "Clan invite sent." );
	}

	private void HostAcceptInvite()
	{
		var caller = Rpc.Caller;
		var player = GetCallerPlayer( caller );
		if ( !player.IsValid() )
			return;

		if ( player.ClanId >= 0 )
		{
			ClearInviteFor( caller, -1 );
			Notify( caller, false, "You are already in a clan." );
			return;
		}

		if ( !_pendingInvites.TryGetValue( caller.SteamId.Value, out var invite ) )
		{
			ClearInviteFor( caller, -1 );
			Notify( caller, false, "Invite expired." );
			return;
		}

		var clan = FindClan( invite.ClanId );
		if ( clan is null )
		{
			_pendingInvites.Remove( caller.SteamId.Value );
			ClearInviteFor( caller, -1 );
			Notify( caller, false, "Clan no longer exists." );
			return;
		}

		var steamId = caller.SteamId.Value;
		if ( !clan.MemberSteamIds.Contains( steamId ) )
			clan.MemberSteamIds.Add( steamId );

		clan.SetRank( steamId, ClanRank.Soldier );
		_pendingInvites.Remove( steamId );
		ApplyClanToPlayer( player, clan, ClanRank.Soldier );
		SaveAll();
		ClearInviteFor( caller, clan.Id );
		Notify( caller, true, $"Joined clan: {clan.Header}." );
	}

	private void HostDeclineInvite()
	{
		var caller = Rpc.Caller;
		if ( caller is null )
			return;

		var clanId = _pendingInvites.TryGetValue( caller.SteamId.Value, out var invite ) ? invite.ClanId : -1;
		_pendingInvites.Remove( caller.SteamId.Value );
		ClearInviteFor( caller, clanId );
		Notify( caller, true, "Clan invite declined." );
	}

	private void HostKickMember( long targetSteamId )
	{
		var caller = Rpc.Caller;
		var actor = GetCallerPlayer( caller );
		var clan = GetClanForPlayer( actor );
		if ( clan is null || targetSteamId == 0L )
			return;

		if ( !CanManageMember( clan, caller.SteamId.Value, targetSteamId, out var message ) )
		{
			Notify( caller, false, message );
			return;
		}

		RemoveMember( clan, targetSteamId );
		var target = Player.FindPlayerBySteamId( targetSteamId );
		if ( target.IsValid() )
			ClearClanFromPlayer( target );

		SaveAll();
		Notify( caller, true, "Clan member kicked." );
	}

	private void HostSetMemberRank( long targetSteamId, int rankValue )
	{
		var caller = Rpc.Caller;
		var actor = GetCallerPlayer( caller );
		var clan = GetClanForPlayer( actor );
		if ( clan is null || targetSteamId == 0L )
			return;

		if ( !Enum.IsDefined( typeof( ClanRank ), rankValue ) )
			return;

		var newRank = (ClanRank)rankValue;
		if ( newRank == ClanRank.Leader )
		{
			Notify( caller, false, "Leader rank cannot be assigned here." );
			return;
		}

		if ( !CanManageMember( clan, caller.SteamId.Value, targetSteamId, out var message ) )
		{
			Notify( caller, false, message );
			return;
		}

		var actorRank = clan.GetRank( caller.SteamId.Value );
		if ( actorRank == ClanRank.DeputyLeader && newRank >= ClanRank.DeputyLeader )
		{
			Notify( caller, false, "Deputy leader cannot assign deputy rank." );
			return;
		}

		clan.SetRank( targetSteamId, newRank );
		var target = Player.FindPlayerBySteamId( targetSteamId );
		if ( target.IsValid() )
			ApplyClanToPlayer( target, clan, newRank );

		SaveAll();
		Notify( caller, true, "Clan rank updated." );
	}

	public void HostApplyLoadedPlayerClan( Player player, int? savedClanId, string savedRank )
	{
		if ( !Networking.IsHost || !player.IsValid() )
			return;

		if ( savedClanId is null || savedClanId.Value < 0 )
		{
			ClearClanFromPlayer( player, savePlayer: false );
			return;
		}

		var steamId = player.GameObject.Network.Owner?.SteamId.Value ?? 0L;
		var clan = FindClan( savedClanId.Value );
		if ( clan is null || steamId == 0L || !clan.MemberSteamIds.Contains( steamId ) )
		{
			ClearClanFromPlayer( player );
			return;
		}

		ApplyClanToPlayer( player, clan, clan.GetRank( steamId ), savePlayer: false );
		HostPublishSummaries();
	}

	public void HostNotifyPlayerDisconnected( long steamId )
	{
		if ( !Networking.IsHost || steamId == 0L )
			return;

		_pendingInvites.Remove( steamId );
		var clan = _clans.FirstOrDefault( x => x.LeaderSteamId == steamId );
		if ( clan is not null )
		{
			clan.LeaderLastSeenRaw = DateTimeOffset.UtcNow.ToString( "O" );
			SaveAll();
			return;
		}

		HostPublishSummaries();
	}

	public void HostRecordKill( Player attacker )
	{
		if ( !Networking.IsHost || !attacker.IsValid() || attacker.ClanId < 0 )
			return;

		var clan = FindClan( attacker.ClanId );
		if ( clan is null )
			return;

		clan.Kills++;
		SaveAll();
	}

	private void DeleteClan( Clan clan, string message )
	{
		if ( clan is null )
			return;

		var memberIds = new List<long>( clan.MemberSteamIds );
		_clans.Remove( clan );
		foreach ( var steamId in memberIds )
		{
			var member = Player.FindPlayerBySteamId( steamId );
			if ( member.IsValid() )
				ClearClanFromPlayer( member );

			_pendingInvites.Remove( steamId );
		}

		var inviteTargets = _pendingInvites
			.Where( x => x.Value.ClanId == clan.Id )
			.Select( x => x.Key )
			.ToList();
		foreach ( var pair in _pendingInvites.Where( x => x.Value.ClanId == clan.Id ).ToList() )
		{
			_pendingInvites.Remove( pair.Key );
		}

		SaveAll();
		ClearInviteForSteamIds( inviteTargets, clan.Id );
		NotifyClanMembers( memberIds, true, message );
	}

	private void ApplyClanToOnlineMembers( Clan clan )
	{
		foreach ( var steamId in clan.MemberSteamIds )
		{
			var player = Player.FindPlayerBySteamId( steamId );
			if ( player.IsValid() )
				ApplyClanToPlayer( player, clan, clan.GetRank( steamId ) );
		}
	}

	private void ApplyClanToPlayer( Player player, Clan clan, ClanRank rank, bool savePlayer = true )
	{
		if ( !player.IsValid() || clan is null )
			return;

		player.HostSetClanState( clan.Id, clan.Header, clan.ColorId, rank );
		if ( savePlayer )
			player.HostSavePlayerData();
	}

	private void ClearClanFromPlayer( Player player, bool savePlayer = true )
	{
		if ( !player.IsValid() )
			return;

		player.HostClearClanState();
		if ( savePlayer )
			player.HostSavePlayerData();
	}

	private void RemoveMember( Clan clan, long steamId )
	{
		clan.MemberSteamIds.RemoveAll( x => x == steamId );
		clan.MemberRanks.Remove( steamId );
		_pendingInvites.Remove( steamId );
	}

	private void SaveAll()
	{
		foreach ( var clan in _clans )
		{
			if ( clan.LeaderSteamId != 0L && Player.FindPlayerBySteamId( clan.LeaderSteamId ).IsValid() )
				clan.LeaderLastSeenRaw = DateTimeOffset.UtcNow.ToString( "O" );

			clan.Normalize();
		}

		ClanDatabase.Save( _clans );
		HostPublishSummaries();
	}

	private void HostPublishSummaries()
	{
		var summaries = new List<ClanSummary>();
		foreach ( var clan in _clans )
		{
			var onlineMembers = new List<ClanMemberSummary>();
			foreach ( var steamId in clan.MemberSteamIds )
			{
				var player = Player.FindPlayerBySteamId( steamId );
				if ( !player.IsValid() )
					continue;

				onlineMembers.Add( new ClanMemberSummary
				{
					SteamId = steamId,
					Name = player.GameObject.Network.Owner?.DisplayName ?? "",
					Rank = clan.GetRank( steamId )
				} );
			}

			summaries.Add( new ClanSummary
			{
				Id = clan.Id,
				Header = clan.Header,
				ColorId = clan.ColorId,
				Description = clan.Description,
				Logo = clan.Logo,
				Balance = clan.Balance,
				MemberCount = clan.MemberCount,
				Kills = clan.Kills,
				LeaderLastSeenRaw = clan.LeaderLastSeenRaw,
				LeaderSteamId = clan.LeaderSteamId,
				LeaderOnline = onlineMembers.Any( x => x.SteamId == clan.LeaderSteamId ),
				OnlineMembers = onlineMembers
			} );
		}

		var json = JsonSerializer.Serialize( summaries );
		SetClanSummariesJson( json );
		RpcReceiveClanSummaries( json, ClanVersion );
	}

	private bool CanManageMember( Clan clan, long actorSteamId, long targetSteamId, out string message )
	{
		message = "";
		if ( actorSteamId == targetSteamId )
		{
			message = "Use leave clan instead.";
			return false;
		}

		if ( targetSteamId == clan.LeaderSteamId )
		{
			message = "Leader cannot be managed.";
			return false;
		}

		var actorRank = clan.GetRank( actorSteamId );
		var targetRank = clan.GetRank( targetSteamId );
		if ( actorRank == ClanRank.Leader )
			return true;

		if ( actorRank == ClanRank.DeputyLeader && targetRank < ClanRank.DeputyLeader )
			return true;

		message = "You cannot manage this member.";
		return false;
	}

	private ClanLogo BuildLogo( string iconPath, string backgroundType, string backgroundColor1, string backgroundColor2, string backgroundColor3, string backgroundColor4 )
	{
		if ( !Enum.TryParse<ClanBackgroundType>( backgroundType ?? "", true, out var parsedType ) )
			parsedType = ClanBackgroundType.Mono;

		var logo = new ClanLogo
		{
			IconPath = iconPath ?? "",
			BackgroundType = parsedType,
			BackgroundColor1 = backgroundColor1,
			BackgroundColor2 = backgroundColor2,
			BackgroundColor3 = backgroundColor3,
			BackgroundColor4 = backgroundColor4
		};
		logo.Normalize();
		return logo;
	}

	private int GetNextClanId()
	{
		var used = new HashSet<int>( _clans.Select( x => x.Id ) );
		var id = 0;
		while ( used.Contains( id ) )
			id++;

		return id;
	}

	private Clan FindClan( int clanId ) => _clans.FirstOrDefault( x => x.Id == clanId );

	private Clan GetClanForPlayer( Player player )
	{
		if ( !player.IsValid() || player.ClanId < 0 )
			return null;

		return FindClan( player.ClanId );
	}

	private Player GetCallerPlayer( Connection caller )
	{
		if ( caller is null )
			return null;

		var player = Player.FindPlayerBySteamId( caller.SteamId.Value );
		return player.IsValid() && player.GameObject.Network.Owner == caller ? player : null;
	}

	private void ClearInviteFor( Connection connection, int clanId )
	{
		if ( connection is null )
			return;

		using ( Rpc.FilterInclude( c => c.SteamId.Value == connection.SteamId.Value ) )
		{
			RpcClearInvite( clanId );
		}
	}

	private void Notify( Connection connection, bool success, string message )
	{
		if ( connection is null )
			return;

		using ( Rpc.FilterInclude( c => c.SteamId.Value == connection.SteamId.Value ) )
		{
			RpcReceiveClanResult( message, success );
		}
	}

	private void NotifyClanMembers( IReadOnlyList<long> steamIds, bool success, string message )
	{
		if ( steamIds is null || steamIds.Count == 0 )
			return;

		var set = new HashSet<long>( steamIds );
		using ( Rpc.FilterInclude( c => set.Contains( c.SteamId.Value ) ) )
		{
			RpcReceiveClanResult( message, success );
			RpcClearInvite( -1 );
		}
	}

	private void ClearInviteForSteamIds( IReadOnlyList<long> steamIds, int clanId )
	{
		if ( steamIds is null || steamIds.Count == 0 )
			return;

		var set = new HashSet<long>( steamIds );
		using ( Rpc.FilterInclude( c => set.Contains( c.SteamId.Value ) ) )
		{
			RpcClearInvite( clanId );
		}
	}

	private sealed class PendingClanInvite
	{
		public int ClanId { get; set; }
		public long InviterSteamId { get; set; }
		public string InviterName { get; set; } = "";
	}
}
