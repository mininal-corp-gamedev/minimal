using Sandbox;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Minimal.Clan;

public sealed class AdminManager : Component, Component.INetworkListener
{
	public const int PlayerRank = 0;
	public const int ModeratorRank = 1;
	public const int AdministratorRank = 2;
	public const int SuperAdministratorRank = 3;

	private const string AdminFolder = "admin";
	private const string BanRootFolder = "Admin";
	private const string BanFolder = "Admin/Bans";
	private const float DoorTraceDistance = 350f;

	private static readonly Dictionary<long, Transform> ReturnTransforms = new();

	public static AdminManager Instance { get; private set; }

	protected override void OnStart()
	{
		Instance = this;
#if SERVER
		EnsureFolders();
#endif
	}

	protected override void OnDestroy()
	{
		if ( Instance == this )
			Instance = null;
	}

	public static string GetRankName( int rank ) => rank switch
	{
		SuperAdministratorRank => GameLocalization.Phrase( "admin.rank.superadmin", "Super Administrator" ),
		AdministratorRank => GameLocalization.Phrase( "admin.rank.admin", "Administrator" ),
		ModeratorRank => GameLocalization.Phrase( "admin.rank.moderator", "Moderator" ),
		_ => GameLocalization.Phrase( "admin.rank.player", "Player" )
	};

	[Rpc.Host]
	public static void RpcRequestRankInit()
	{
#if SERVER
		var caller = Rpc.Caller;
		if ( caller is null )
			return;

		if ( IsBanned( caller.SteamId.Value, out var ban ) )
		{
			CloseBannedClient( caller, ban.Reason );
			return;
		}

		var player = FindPlayerBySteamId( caller.SteamId.Value );
		if ( !player.IsValid() )
			return;

		player.AdminRank = LoadRank( caller.SteamId.Value ).Rank;
#endif
	}

	[Rpc.Host] public static void RpcRequestKick( long steamId, string reason )
	{
#if SERVER
		Kick( Rpc.Caller, steamId, reason );
#endif
	}

	[Rpc.Host] public static void RpcRequestBan( long steamId, string reason )
	{
#if SERVER
		Ban( Rpc.Caller, steamId, reason );
#endif
	}

	[Rpc.Host] public static void RpcRequestUnban( long steamId )
	{
#if SERVER
		Unban( Rpc.Caller, steamId );
#endif
	}

	[Rpc.Host] public static void RpcRequestSpawn( long steamId )
	{
#if SERVER
		Respawn( Rpc.Caller, steamId );
#endif
	}

	[Rpc.Host] public static void RpcRequestSetMoney( long steamId, int value )
	{
#if SERVER
		SetMoney( Rpc.Caller, steamId, value );
#endif
	}

	[Rpc.Host] public static void RpcRequestSetHp( long steamId, float value )
	{
#if SERVER
		SetHp( Rpc.Caller, steamId, value );
#endif
	}

	[Rpc.Host] public static void RpcRequestKill( long steamId )
	{
#if SERVER
		Kill( Rpc.Caller, steamId );
#endif
	}

	[Rpc.Host] public static void RpcRequestSetJob( long steamId, string jobId )
	{
#if SERVER
		SetJob( Rpc.Caller, steamId, jobId );
#endif
	}

	[Rpc.Host] public static void RpcRequestSellDoor()
	{
#if SERVER
		SellDoor( Rpc.Caller );
#endif
	}

	[Rpc.Host] public static void RpcRequestSellDoorAt( Vector3 eyePosition, Vector3 eyeForward )
	{
#if SERVER
		SellDoor( Rpc.Caller, eyePosition, eyeForward );
#endif
	}

	[Rpc.Host] public static void RpcRequestGoto( long steamId )
	{
#if SERVER
		Goto( Rpc.Caller, steamId );
#endif
	}

	[Rpc.Host] public static void RpcRequestTp( long steamId )
	{
#if SERVER
		TeleportToCaller( Rpc.Caller, steamId );
#endif
	}

	[Rpc.Host] public static void RpcRequestReturn( long steamId )
	{
#if SERVER
		Return( Rpc.Caller, steamId );
#endif
	}

	[Rpc.Host] public static void RpcRequestGiveRank( long steamId, int rank )
	{
#if SERVER
		GiveRank( Rpc.Caller, steamId, rank );
#endif
	}

	[Rpc.Host] public static void RpcRequestPrintInventory( long steamId )
	{
#if SERVER
		PrintInventory( Rpc.Caller, steamId );
#endif
	}

	[Rpc.Host] public static void RpcRequestClearInventory( long steamId )
	{
#if SERVER
		ClearInventory( Rpc.Caller, steamId );
#endif
	}

	[Rpc.Host] public static void RpcRequestRemoveClan( int clanId )
	{
#if SERVER
		RemoveClan( Rpc.Caller, clanId );
#endif
	}

	[ConCmd( "adm", ConVarFlags.Server )]
	public static void AdmCommand( Connection caller, string command = "", string steamIdText = "", string value = "", string extra1 = "", string extra2 = "", string extra3 = "", string extra4 = "", string extra5 = "", string extra6 = "", string extra7 = "", string extra8 = "" )
	{
#if SERVER
		var argsTail = JoinArgs( value, extra1, extra2, extra3, extra4, extra5, extra6, extra7, extra8 );
		if ( !TryParseSteamId( steamIdText, out var steamId ) && !string.Equals( command, "selldoor", StringComparison.OrdinalIgnoreCase ) )
		{
			NotifyCaller( caller, GameLocalization.Phrase( "notify.admin.invalid_steamid", "Invalid SteamId." ), AdminNotifyType.Error );
			return;
		}

		switch ( (command ?? "").ToLowerInvariant() )
		{
			case "kick":
				Kick( caller, steamId, argsTail );
				break;
			case "ban":
				Ban( caller, steamId, argsTail );
				break;
			case "unban":
				Unban( caller, steamId );
				break;
			case "spawn":
				Respawn( caller, steamId );
				break;
			case "setmoney":
				if ( int.TryParse( value, out var money ) ) SetMoney( caller, steamId, money );
				else NotifyCaller( caller, GameLocalization.Phrase( "notify.admin.invalid_money", "Invalid money value." ), AdminNotifyType.Error );
				break;
			case "sethp":
				if ( float.TryParse( value, out var hp ) ) SetHp( caller, steamId, hp );
				else NotifyCaller( caller, GameLocalization.Phrase( "notify.admin.invalid_hp", "Invalid hp value." ), AdminNotifyType.Error );
				break;
			case "kill":
				Kill( caller, steamId );
				break;
			case "setjob":
				SetJob( caller, steamId, value );
				break;
			case "selldoor":
				SellDoor( caller );
				break;
			case "goto":
				Goto( caller, steamId );
				break;
			case "tp":
				TeleportToCaller( caller, steamId );
				break;
			case "return":
				Return( caller, steamId );
				break;
			case "giverank":
				if ( int.TryParse( value, out var rank ) ) GiveRank( caller, steamId, rank );
				else NotifyCaller( caller, GameLocalization.Phrase( "notify.admin.invalid_rank", "Invalid rank value." ), AdminNotifyType.Error );
				break;
			case "inv":
			case "inventory":
				PrintInventory( caller, steamId );
				break;
			case "clearinv":
			case "clearinventory":
				ClearInventory( caller, steamId );
				break;
			default:
				NotifyCaller( caller, GameLocalization.Phrase( "notify.admin.usage", "Usage: adm <kick|ban|unban|spawn|setmoney|sethp|kill|setjob|selldoor|goto|tp|return|giverank|inv|clearinv> ..." ), AdminNotifyType.Warn );
				break;
		}
#endif
	}

	public bool AcceptConnection( Connection channel, string reason )
	{
#if SERVER
		if ( channel is null )
			return true;

		return !IsBanned( channel.SteamId.Value, out _ );
#else
		return true;
#endif
	}

	public void OnConnected( Connection channel )
	{
#if SERVER
		if ( channel is null )
			return;

		if ( IsBanned( channel.SteamId.Value, out var ban ) )
			CloseBannedClient( channel, ban.Reason );
#endif
	}

	public void OnActive( Connection channel )
	{
#if SERVER
		if ( channel is null )
			return;

		if ( IsBanned( channel.SteamId.Value, out var ban ) )
		{
			CloseBannedClient( channel, ban.Reason );
			return;
		}

		var player = FindPlayerBySteamId( channel.SteamId.Value );
		if ( player.IsValid() )
			player.AdminRank = LoadRank( channel.SteamId.Value ).Rank;

		Roulette.HostSyncAllToConnection( channel );
		Boombox.HostSyncAllToConnection( channel );
#endif
	}

	public void OnDisconnected( Connection channel )
	{
	}

#if SERVER
	private static void Kick( Connection caller, long steamId, string reason )
	{
		if ( !HasAccess( caller, ModeratorRank, out var error ) )
		{
			NotifyCaller( caller, error, AdminNotifyType.Error );
			return;
		}

		var targetConnection = FindConnectionBySteamId( steamId );
		if ( targetConnection is null )
		{
			NotifyCaller( caller, GameLocalization.Phrase( "notify.admin.player_offline", "Player is not online." ), AdminNotifyType.Error );
			return;
		}

		if ( targetConnection.IsHost )
		{
			NotifyCaller( caller, GameLocalization.Phrase( "notify.admin.host_cannot_kick", "Host connection cannot be kicked." ), AdminNotifyType.Error );
			return;
		}

		var normalizedReason = NormalizeReason( reason, GameLocalization.Phrase( "notify.admin.reason.kicked_by_admin", "Kicked by admin" ) );
		if ( !TryKickConnection( targetConnection, normalizedReason, out var kickError ) )
		{
			NotifyCaller( caller, kickError, AdminNotifyType.Error );
			return;
		}

		NotifyCaller( caller, GameLocalization.Format( "notify.admin.kicked", "Kicked {0}: {1}", targetConnection.DisplayName, normalizedReason ), AdminNotifyType.Info );
	}

	private static void Ban( Connection caller, long steamId, string reason )
	{
		if ( !HasAccess( caller, AdministratorRank, out var error ) )
		{
			NotifyCaller( caller, error, AdminNotifyType.Error );
			return;
		}

		var normalizedReason = NormalizeReason( reason, GameLocalization.Phrase( "notify.admin.reason.banned_by_admin", "Banned by admin" ) );
		var targetConnection = FindConnectionBySteamId( steamId );
		if ( targetConnection is not null && targetConnection.IsHost )
		{
			NotifyCaller( caller, GameLocalization.Phrase( "notify.admin.host_cannot_ban", "Host connection cannot be banned." ), AdminNotifyType.Error );
			return;
		}

		// TODO: Add ban duration support. For now every ban is permanent.
		SaveBan( new AdminBanRecord
		{
			SteamId = steamId,
			Reason = normalizedReason,
			BannedAt = DateTimeOffset.UtcNow,
			BannedBySteamId = caller is null ? 0 : caller.SteamId.Value,
			BannedByName = caller?.DisplayName ?? GameLocalization.Phrase( "ui.chat.system", "System" ),
			IsPermanent = true
		} );

		if ( targetConnection is not null )
			CloseBannedClient( targetConnection, normalizedReason );

		NotifyCaller( caller, GameLocalization.Format( "notify.admin.banned", "Banned {0}: {1}", steamId, normalizedReason ), AdminNotifyType.Info );
	}

	private static void Unban( Connection caller, long steamId )
	{
		if ( !HasAccess( caller, AdministratorRank, out var error ) )
		{
			NotifyCaller( caller, error, AdminNotifyType.Error );
			return;
		}

		var path = GetBanPath( steamId );
		if ( !FileSystem.Data.FileExists( path ) )
		{
			NotifyCaller( caller, GameLocalization.Phrase( "notify.admin.steamid_not_banned", "SteamId is not banned." ), AdminNotifyType.Warn );
			return;
		}

		FileSystem.Data.DeleteFile( path );
		NotifyCaller( caller, GameLocalization.Format( "notify.admin.unbanned", "Unbanned {0}.", steamId ), AdminNotifyType.Info );
	}

	private static void Respawn( Connection caller, long steamId )
	{
		if ( !HasAccess( caller, ModeratorRank, out var error ) )
		{
			NotifyCaller( caller, error, AdminNotifyType.Error );
			return;
		}

		var target = FindPlayerBySteamId( steamId );
		if ( !target.IsValid() )
		{
			NotifyCaller( caller, GameLocalization.Phrase( "notify.admin.player_offline", "Player is not online." ), AdminNotifyType.Error );
			return;
		}

		target.HostTriggerRespawn();
		NotifyCaller( caller, GameLocalization.Format( "notify.admin.respawned", "Respawned {0}.", GetPlayerName( target ) ), AdminNotifyType.Info );
	}

	private static void SetMoney( Connection caller, long steamId, int value )
	{
		if ( !HasAccess( caller, AdministratorRank, out var error ) )
		{
			NotifyCaller( caller, error, AdminNotifyType.Error );
			return;
		}

		var target = FindPlayerBySteamId( steamId );
		if ( !target.IsValid() )
		{
			NotifyCaller( caller, GameLocalization.Phrase( "notify.admin.player_offline", "Player is not online." ), AdminNotifyType.Error );
			return;
		}

		target.Money = Math.Max( 0, value );
		NotifyCaller( caller, GameLocalization.Format( "notify.admin.set_money", "Set {0} money to ${1}.", GetPlayerName( target ), target.Money ), AdminNotifyType.Info );
	}

	private static void SetHp( Connection caller, long steamId, float value )
	{
		if ( !HasAccess( caller, AdministratorRank, out var error ) )
		{
			NotifyCaller( caller, error, AdminNotifyType.Error );
			return;
		}

		var target = FindPlayerBySteamId( steamId );
		if ( !target.IsValid() )
		{
			NotifyCaller( caller, GameLocalization.Phrase( "notify.admin.player_offline", "Player is not online." ), AdminNotifyType.Error );
			return;
		}

		target.Health = Math.Clamp( value, 0f, target.MaxHealth );
		if ( target.Health <= 0f )
		{
			target.HostKill( GameLocalization.Phrase( "ui.hud.killed_by_admin", "You were killed by an administrator." ) );
			NotifyCaller( caller, GameLocalization.Format( "notify.admin.killed_by_set_hp", "Killed {0} by setting hp to 0.", GetPlayerName( target ) ), AdminNotifyType.Info );
			return;
		}

		target.WorldHud?.WorldHudRefresh();
		NotifyCaller( caller, GameLocalization.Format( "notify.admin.set_hp", "Set {0} hp to {1}.", GetPlayerName( target ), target.Health.ToString( "0", CultureInfo.InvariantCulture ) ), AdminNotifyType.Info );
	}

	private static void Kill( Connection caller, long steamId )
	{
		if ( !HasAccess( caller, AdministratorRank, out var error ) )
		{
			NotifyCaller( caller, error, AdminNotifyType.Error );
			return;
		}

		var target = FindPlayerBySteamId( steamId );
		if ( !target.IsValid() )
		{
			NotifyCaller( caller, GameLocalization.Phrase( "notify.admin.player_offline", "Player is not online." ), AdminNotifyType.Error );
			return;
		}

		if ( target.IsDead )
		{
			NotifyCaller( caller, GameLocalization.Phrase( "notify.admin.player_already_dead", "Player is already dead." ), AdminNotifyType.Warn );
			return;
		}

		target.HostKill( GameLocalization.Phrase( "ui.hud.killed_by_admin", "You were killed by an administrator." ) );
		NotifyCaller( caller, GameLocalization.Format( "notify.admin.killed", "Killed {0}.", GetPlayerName( target ) ), AdminNotifyType.Info );
	}

	private static void SetJob( Connection caller, long steamId, string jobId )
	{
		if ( !HasAccess( caller, AdministratorRank, out var error ) )
		{
			NotifyCaller( caller, error, AdminNotifyType.Error );
			return;
		}

		var normalizedJobId = (jobId ?? "").Trim();
		if ( string.IsNullOrEmpty( normalizedJobId ) || JobDatabase.Get( normalizedJobId ) is null )
		{
			NotifyCaller( caller, GameLocalization.Phrase( "notify.jobs.not_found", "Job not found." ), AdminNotifyType.Error );
			return;
		}

		var target = FindPlayerBySteamId( steamId );
		if ( !target.IsValid() )
		{
			NotifyCaller( caller, GameLocalization.Phrase( "notify.admin.player_offline", "Player is not online." ), AdminNotifyType.Error );
			return;
		}

		target.Job?.HostSetJob( normalizedJobId );
		NotifyCaller( caller, GameLocalization.Format( "notify.admin.set_job", "Set {0} job to {1}.", GetPlayerName( target ), normalizedJobId ), AdminNotifyType.Info );
	}

	private static void SellDoor( Connection caller )
	{
		SellDoor( caller, null, null );
	}

	private static void SellDoor( Connection caller, Vector3? requestedEyePosition, Vector3? requestedEyeForward )
	{
		if ( !HasAccess( caller, ModeratorRank, out var error ) )
		{
			NotifyCaller( caller, error, AdminNotifyType.Error );
			return;
		}

		if ( caller is null )
		{
			NotifyCaller( caller, GameLocalization.Phrase( "notify.admin.command_requires_player", "This command requires an in-game admin player." ), AdminNotifyType.Error );
			return;
		}

		var admin = FindPlayerBySteamId( caller.SteamId.Value );
		if ( !admin.IsValid() || !admin.Controller.IsValid() )
		{
			NotifyCaller( caller, GameLocalization.Phrase( "notify.admin.admin_player_offline", "Admin player is not online." ), AdminNotifyType.Error );
			return;
		}

		var scene = Game.ActiveScene;
		if ( scene is null )
		{
			NotifyCaller( caller, GameLocalization.Phrase( "notify.admin.active_scene_not_found", "Active scene not found." ), AdminNotifyType.Error );
			return;
		}

		var eye = admin.Controller.EyeTransform;
		var traceOrigin = requestedEyePosition ?? eye.Position;
		var traceForward = requestedEyeForward.HasValue && requestedEyeForward.Value.LengthSquared > 0.001f
			? requestedEyeForward.Value.Normal
			: eye.Forward;

		if ( requestedEyePosition.HasValue && Vector3.DistanceBetween( admin.WorldPosition, traceOrigin ) > 200f )
		{
			NotifyCaller( caller, GameLocalization.Phrase( "notify.admin.door_trace_too_far", "Door trace origin is too far from you." ), AdminNotifyType.Error );
			return;
		}

		var trace = scene.Trace
			.Ray( traceOrigin, traceOrigin + traceForward * DoorTraceDistance )
			.IgnoreGameObjectHierarchy( admin.GameObject )
			.Run();

		var door = FindDoorFromTrace( trace );
		if ( !door.IsValid() )
		{
			NotifyCaller( caller, GameLocalization.Phrase( "notify.admin.look_at_door", "Look at a door first." ), AdminNotifyType.Error );
			return;
		}

		if ( door.IsBlocked || door.HasOnlyJobs || door.LockState == Door.DoorLockState.Locked || !door.HasOwner )
		{
			NotifyCaller( caller, GameLocalization.Phrase( "notify.admin.door_force_sell_denied", "This door cannot be force-sold." ), AdminNotifyType.Error );
			return;
		}

		door.Sell();
		NotifyCaller( caller, GameLocalization.Phrase( "notify.admin.door_force_sold", "Door was force-sold." ), AdminNotifyType.Info );
	}

	private static void Goto( Connection caller, long steamId )
	{
		if ( !HasAccess( caller, ModeratorRank, out var error ) )
		{
			NotifyCaller( caller, error, AdminNotifyType.Error );
			return;
		}

		if ( caller is null )
		{
			NotifyCaller( caller, GameLocalization.Phrase( "notify.admin.command_requires_player", "This command requires an in-game admin player." ), AdminNotifyType.Error );
			return;
		}

		var admin = FindPlayerBySteamId( caller.SteamId.Value );
		var target = FindPlayerBySteamId( steamId );
		if ( !admin.IsValid() || !target.IsValid() )
		{
			NotifyCaller( caller, GameLocalization.Phrase( "notify.admin.player_offline", "Player is not online." ), AdminNotifyType.Error );
			return;
		}

		SaveReturnTransform( admin );
		var gotoPos = target.WorldPosition + target.WorldRotation.Backward * 64f;
		var gotoRot = Rotation.LookAt( target.WorldPosition - gotoPos );
		admin.HostTeleport( gotoPos, gotoRot );
		NotifyCaller( caller, GameLocalization.Format( "notify.admin.teleported_to", "Teleported to {0}.", GetPlayerName( target ) ), AdminNotifyType.Info );
	}

	private static void TeleportToCaller( Connection caller, long steamId )
	{
		if ( !HasAccess( caller, ModeratorRank, out var error ) )
		{
			NotifyCaller( caller, error, AdminNotifyType.Error );
			return;
		}

		if ( caller is null )
		{
			NotifyCaller( caller, GameLocalization.Phrase( "notify.admin.command_requires_player", "This command requires an in-game admin player." ), AdminNotifyType.Error );
			return;
		}

		var admin = FindPlayerBySteamId( caller.SteamId.Value );
		var target = FindPlayerBySteamId( steamId );
		if ( !admin.IsValid() || !target.IsValid() )
		{
			NotifyCaller( caller, GameLocalization.Phrase( "notify.admin.player_offline", "Player is not online." ), AdminNotifyType.Error );
			return;
		}

		SaveReturnTransform( target );
		var tpPos = admin.WorldPosition + admin.WorldRotation.Forward * 64f;
		var tpRot = Rotation.LookAt( admin.WorldPosition - tpPos );
		target.HostTeleport( tpPos, tpRot );
		NotifyCaller( caller, GameLocalization.Format( "notify.admin.teleported_to_you", "Teleported {0} to you.", GetPlayerName( target ) ), AdminNotifyType.Info );
	}

	private static void Return( Connection caller, long steamId )
	{
		if ( !HasAccess( caller, ModeratorRank, out var error ) )
		{
			NotifyCaller( caller, error, AdminNotifyType.Error );
			return;
		}

		var target = FindPlayerBySteamId( steamId );
		if ( !target.IsValid() )
		{
			NotifyCaller( caller, GameLocalization.Phrase( "notify.admin.player_offline", "Player is not online." ), AdminNotifyType.Error );
			return;
		}

		if ( !ReturnTransforms.TryGetValue( steamId, out var transform ) )
		{
			NotifyCaller( caller, GameLocalization.Phrase( "notify.admin.no_return_position", "No saved return position for this player." ), AdminNotifyType.Warn );
			return;
		}

		target.HostTeleport( transform.Position, transform.Rotation );
		ReturnTransforms.Remove( steamId );
		NotifyCaller( caller, GameLocalization.Format( "notify.admin.returned", "Returned {0}.", GetPlayerName( target ) ), AdminNotifyType.Info );
	}

	private static void GiveRank( Connection caller, long steamId, int rank )
	{
		if ( !HasAccess( caller, SuperAdministratorRank, out var error ) )
		{
			NotifyCaller( caller, error, AdminNotifyType.Error );
			return;
		}

		var clampedRank = Math.Clamp( rank, PlayerRank, SuperAdministratorRank );
		SaveRank( new AdminRankRecord
		{
			SteamId = steamId,
			Rank = clampedRank,
			GrantedAt = DateTimeOffset.UtcNow,
			GrantedBySteamId = caller is null ? 0 : caller.SteamId.Value
		} );

		var target = FindPlayerBySteamId( steamId );
		if ( target.IsValid() )
			target.AdminRank = clampedRank;

		NotifyCaller( caller, GameLocalization.Format( "notify.admin.set_rank", "Set {0} rank to {1}.", steamId, GetRankName( clampedRank ) ), AdminNotifyType.Info );
	}

	private static void PrintInventory( Connection caller, long steamId )
	{
		if ( !HasAccess( caller, AdministratorRank, out var error ) )
		{
			NotifyCaller( caller, error, AdminNotifyType.Error );
			return;
		}

		var target = FindPlayerBySteamId( steamId );
		if ( !target.IsValid() )
		{
			NotifyCaller( caller, GameLocalization.Phrase( "notify.admin.player_offline", "Player is not online." ), AdminNotifyType.Error );
			return;
		}

		PrintInventoryLog( caller, BuildInventoryLog( target ) );
		NotifyCaller( caller, GameLocalization.Format( "notify.admin.inventory_printed", "Printed {0}'s inventory to console.", GetPlayerName( target ) ), AdminNotifyType.Info );
	}

	private static void ClearInventory( Connection caller, long steamId )
	{
		if ( !HasAccess( caller, AdministratorRank, out var error ) )
		{
			NotifyCaller( caller, error, AdminNotifyType.Error );
			return;
		}

		var target = FindPlayerBySteamId( steamId );
		if ( !target.IsValid() )
		{
			NotifyCaller( caller, GameLocalization.Phrase( "notify.admin.player_offline", "Player is not online." ), AdminNotifyType.Error );
			return;
		}

		var removed = target.HostClearInventoryExceptDefaultItems();
		NotifyCaller( caller, GameLocalization.Format( "notify.admin.inventory_cleared", "Cleared {0}'s inventory. Removed {1} item(s).", GetPlayerName( target ), removed ), AdminNotifyType.Info );
	}

	private static void RemoveClan( Connection caller, int clanId )
	{
		if ( !HasAccess( caller, AdministratorRank, out var error ) )
		{
			NotifyCaller( caller, error, AdminNotifyType.Error );
			return;
		}

		if ( clanId < 0 )
		{
			NotifyCaller( caller, "Invalid clan id.", AdminNotifyType.Error );
			return;
		}

		if ( ClanManager.Instance is null )
		{
			NotifyCaller( caller, "Clan manager is not ready.", AdminNotifyType.Error );
			return;
		}

		var success = ClanManager.Instance.HostAdminDeleteClan( caller, clanId, out var message );
		NotifyCaller( caller, message, success ? AdminNotifyType.Info : AdminNotifyType.Error );
	}

	private static bool HasAccess( Connection caller, int requiredRank, out string error )
	{
		error = null;

		if ( IsServerAuthority( caller ) )
			return true;

		if ( IsBanned( caller.SteamId.Value, out var ban ) )
		{
			error = GameLocalization.Format( "notify.admin.banned_reason", "You are banned: {0}", ban.Reason );
			return false;
		}

		var player = FindPlayerBySteamId( caller.SteamId.Value );
		var rank = player.IsValid() ? player.AdminRank : LoadRank( caller.SteamId.Value ).Rank;
		if ( rank >= requiredRank )
			return true;

		error = GameLocalization.Format( "notify.admin.access_denied_required_rank", "Access denied. Required rank: {0}.", GetRankName( requiredRank ) );
		return false;
	}

	private static bool IsServerAuthority( Connection caller )
	{
		return caller is null || caller.IsHost;
	}

	private static Player FindPlayerBySteamId( long steamId )
	{
		var scene = Game.ActiveScene;
		if ( scene is null )
			return null;

		foreach ( var player in scene.GetAllComponents<Player>() )
		{
			if ( player.GameObject.Network.Owner?.SteamId.Value == steamId )
				return player;
		}

		return null;
	}

	private static Connection FindConnectionBySteamId( long steamId )
	{
		foreach ( var connection in Connection.All )
		{
			if ( connection.SteamId.Value == steamId )
				return connection;
		}

		return null;
	}

	private static Door FindDoorFromTrace( SceneTraceResult trace )
	{
		if ( !trace.Hit )
			return null;

		var go = trace.GameObject;
		while ( go.IsValid() )
		{
			if ( go.Components.TryGet<Door>( out var door ) )
				return door;

			go = go.Parent;
		}

		return null;
	}

	private static void SaveReturnTransform( Player player )
	{
		var owner = player.GameObject.Network.Owner;
		if ( owner is null )
			return;

		ReturnTransforms[owner.SteamId.Value] = player.WorldTransform;
	}

	private static string GetPlayerName( Player player )
	{
		return player.GameObject.Network.Owner?.DisplayName ?? GameLocalization.Phrase( "common.player", "Player" );
	}

	private static string BuildInventoryLog( Player player )
	{
		var builder = new StringBuilder();
		var owner = player.GameObject.Network.Owner;
		builder.AppendLine( $"[Admin] Inventory for {GetPlayerName( player )} ({owner?.SteamId.Value ?? 0}):" );

		if ( player.Inventory is null || player.Inventory.Slots.Count == 0 )
		{
			builder.AppendLine( "  <no slots>" );
			return builder.ToString();
		}

		var hasItems = false;
		for ( var i = 0; i < player.Inventory.Slots.Count; i++ )
		{
			var slot = player.Inventory.Slots[i];
			var item = slot.Item;
			if ( item is null )
			{
				builder.AppendLine( $"  [{i}] <empty>" );
				continue;
			}

			hasItems = true;
			var header = item.Definition is null ? item.Id : GameLocalization.ItemHeader( item.Definition );
			builder.AppendLine( $"  [{i}] {item.Id} ({header}) x{item.Count}" );
		}

		if ( !hasItems )
			builder.AppendLine( "  <empty inventory>" );

		var defaultItems = string.Join( ", ", Player.DefaultInventoryItemIdsReadonly );
		builder.AppendLine( $"  Default items: {defaultItems}" );
		return builder.ToString();
	}

	private static void PrintInventoryLog( Connection caller, string text )
	{
		if ( caller is null )
		{
			Log.Info( text );
			return;
		}

		using ( Rpc.FilterInclude( c => c.SteamId.Value == caller.SteamId.Value ) )
		{
			RpcPrintInventoryLog( text );
		}
	}

	private static bool TryParseSteamId( string text, out long steamId )
	{
		return long.TryParse( (text ?? "").Trim(), out steamId ) && steamId > 0;
	}

	private static string JoinArgs( params string[] args )
	{
		return string.Join( " ", args ?? Array.Empty<string>() ).Trim();
	}

	private static string NormalizeReason( string reason, string fallback )
	{
		var normalized = (reason ?? "").Trim();
		return string.IsNullOrEmpty( normalized ) ? fallback : normalized;
	}

	private static void EnsureFolders()
	{
		FileSystem.Data.CreateDirectory( AdminFolder );
		FileSystem.Data.CreateDirectory( BanRootFolder );
		FileSystem.Data.CreateDirectory( BanFolder );
	}

	private static string GetRankPath( long steamId ) => $"{AdminFolder}/{steamId}.json";
	private static string GetBanPath( long steamId ) => $"{BanFolder}/{steamId}.json";

	private static AdminRankRecord LoadRank( long steamId )
	{
		EnsureFolders();

		try
		{
			var path = GetRankPath( steamId );
			if ( !FileSystem.Data.FileExists( path ) )
				return new AdminRankRecord { SteamId = steamId, Rank = PlayerRank };

			var record = FileSystem.Data.ReadJsonOrDefault<AdminRankRecord>( path );
			if ( record is null )
				return new AdminRankRecord { SteamId = steamId, Rank = PlayerRank };

			record.Rank = Math.Clamp( record.Rank, PlayerRank, SuperAdministratorRank );
			record.SteamId = steamId;
			return record;
		}
		catch ( Exception ex )
		{
			Log.Warning( $"Admin rank load failed for {steamId}: {ex.Message}" );
			return new AdminRankRecord { SteamId = steamId, Rank = PlayerRank };
		}
	}

	private static void SaveRank( AdminRankRecord record )
	{
		EnsureFolders();
		record.Rank = Math.Clamp( record.Rank, PlayerRank, SuperAdministratorRank );
		FileSystem.Data.WriteJson( GetRankPath( record.SteamId ), record );
	}

	private static bool IsBanned( long steamId, out AdminBanRecord record )
	{
		EnsureFolders();
		record = null;

		try
		{
			var path = GetBanPath( steamId );
			if ( !FileSystem.Data.FileExists( path ) )
				return false;

			record = FileSystem.Data.ReadJsonOrDefault<AdminBanRecord>( path );
			return record is not null;
		}
		catch ( Exception ex )
		{
			Log.Warning( $"Admin ban load failed for {steamId}: {ex.Message}" );
			return false;
		}
	}

	private static void SaveBan( AdminBanRecord record )
	{
		EnsureFolders();
		FileSystem.Data.WriteJson( GetBanPath( record.SteamId ), record );
	}

	private static void CloseBannedClient( Connection connection, string reason )
	{
		if ( connection is null )
			return;

		using ( Rpc.FilterInclude( c => c.SteamId.Value == connection.SteamId.Value ) )
		{
			RpcCloseGame( NormalizeReason( reason, GameLocalization.Phrase( "notify.admin.reason.banned_from_server", "Banned from server" ) ) );
		}

		TryKickConnection( connection, NormalizeReason( reason, GameLocalization.Phrase( "notify.admin.reason.banned_from_server", "Banned from server" ) ), out _ );
	}

	private static bool TryKickConnection( Connection connection, string reason, out string error )
	{
		error = null;

		if ( connection is null )
		{
			error = GameLocalization.Phrase( "notify.admin.player_connection_unavailable", "Player connection is not available." );
			return false;
		}

		if ( connection.IsHost )
		{
			error = GameLocalization.Phrase( "notify.admin.host_cannot_kick", "Host connection cannot be kicked." );
			return false;
		}

		try
		{
			connection.Kick( reason );
			return true;
		}
		catch ( Exception ex )
		{
			error = GameLocalization.Format( "notify.admin.failed_kick_connection", "Failed to kick connection: {0}", ex.Message );
			return false;
		}
	}

	private static void NotifyCaller( Connection caller, string text, AdminNotifyType type )
	{
		if ( caller is null )
		{
			var prefix = type == AdminNotifyType.Error ? "ERROR" : type == AdminNotifyType.Warn ? "WARN" : "INFO";
			Log.Info( $"[Admin] {prefix}: {text}" );
			return;
		}

		if ( type == AdminNotifyType.Info )
			Log.Info( $"[Admin] {caller.DisplayName} ({caller.SteamId.Value}): {text}" );

		using ( Rpc.FilterInclude( c => c.SteamId.Value == caller.SteamId.Value ) )
		{
			RpcNotify( text, type );
		}
	}
#endif

	[Rpc.Broadcast]
	private static void RpcNotify( string text, AdminNotifyType type )
	{
		switch ( type )
		{
			case AdminNotifyType.Error:
				Notification.Error( text, 4f );
				break;
			case AdminNotifyType.Warn:
				Notification.Warn( text, 4f );
				break;
			default:
				Notification.Info( text, 3.5f );
				break;
		}
	}

	[Rpc.Broadcast]
	private static void RpcPrintInventoryLog( string text )
	{
		Log.Info( text );
	}

	[Rpc.Broadcast]
	private static void RpcCloseGame( string reason )
	{
		Notification.Error( GameLocalization.Format( "notify.admin.banned_screen", "Banned: {0}", reason ), 5f );
		Game.Close();
	}
}

public enum AdminNotifyType
{
	Info,
	Warn,
	Error
}

public sealed class AdminRankRecord
{
	public long SteamId { get; set; }
	public int Rank { get; set; }
	public DateTimeOffset GrantedAt { get; set; }
	public long GrantedBySteamId { get; set; }
}

public sealed class AdminBanRecord
{
	public long SteamId { get; set; }
	public string Reason { get; set; }
	public DateTimeOffset BannedAt { get; set; }
	public long BannedBySteamId { get; set; }
	public string BannedByName { get; set; }
	public bool IsPermanent { get; set; }
}
