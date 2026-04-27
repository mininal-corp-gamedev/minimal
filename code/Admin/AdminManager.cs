using Sandbox;
using System;
using System.Collections.Generic;

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
		EnsureFolders();
	}

	protected override void OnDestroy()
	{
		if ( Instance == this )
			Instance = null;
	}

	public static string GetRankName( int rank ) => rank switch
	{
		SuperAdministratorRank => "Super Administrator",
		AdministratorRank => "Administrator",
		ModeratorRank => "Moderator",
		_ => "Player"
	};

	[Rpc.Host]
	public static void RpcRequestRankInit()
	{
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
	}

	[Rpc.Host] public static void RpcRequestKick( long steamId, string reason ) => Kick( Rpc.Caller, steamId, reason );
	[Rpc.Host] public static void RpcRequestBan( long steamId, string reason ) => Ban( Rpc.Caller, steamId, reason );
	[Rpc.Host] public static void RpcRequestUnban( long steamId ) => Unban( Rpc.Caller, steamId );
	[Rpc.Host] public static void RpcRequestSpawn( long steamId ) => Respawn( Rpc.Caller, steamId );
	[Rpc.Host] public static void RpcRequestSetMoney( long steamId, int value ) => SetMoney( Rpc.Caller, steamId, value );
	[Rpc.Host] public static void RpcRequestSetHp( long steamId, float value ) => SetHp( Rpc.Caller, steamId, value );
	[Rpc.Host] public static void RpcRequestSetJob( long steamId, string jobId ) => SetJob( Rpc.Caller, steamId, jobId );
	[Rpc.Host] public static void RpcRequestSellDoor() => SellDoor( Rpc.Caller );
	[Rpc.Host] public static void RpcRequestSellDoorAt( Vector3 eyePosition, Vector3 eyeForward ) => SellDoor( Rpc.Caller, eyePosition, eyeForward );
	[Rpc.Host] public static void RpcRequestGoto( long steamId ) => Goto( Rpc.Caller, steamId );
	[Rpc.Host] public static void RpcRequestTp( long steamId ) => TeleportToCaller( Rpc.Caller, steamId );
	[Rpc.Host] public static void RpcRequestReturn( long steamId ) => Return( Rpc.Caller, steamId );
	[Rpc.Host] public static void RpcRequestGiveRank( long steamId, int rank ) => GiveRank( Rpc.Caller, steamId, rank );

	[ConCmd( "adm", ConVarFlags.Server )]
	public static void AdmCommand( Connection caller, string command = "", string steamIdText = "", string value = "", string extra1 = "", string extra2 = "", string extra3 = "", string extra4 = "", string extra5 = "", string extra6 = "", string extra7 = "", string extra8 = "" )
	{
		var argsTail = JoinArgs( value, extra1, extra2, extra3, extra4, extra5, extra6, extra7, extra8 );
		if ( !TryParseSteamId( steamIdText, out var steamId ) && !string.Equals( command, "selldoor", StringComparison.OrdinalIgnoreCase ) )
		{
			NotifyCaller( caller, "Invalid SteamId.", AdminNotifyType.Error );
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
				else NotifyCaller( caller, "Invalid money value.", AdminNotifyType.Error );
				break;
			case "sethp":
				if ( float.TryParse( value, out var hp ) ) SetHp( caller, steamId, hp );
				else NotifyCaller( caller, "Invalid hp value.", AdminNotifyType.Error );
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
				else NotifyCaller( caller, "Invalid rank value.", AdminNotifyType.Error );
				break;
			default:
				NotifyCaller( caller, "Usage: adm <kick|ban|unban|spawn|setmoney|sethp|setjob|selldoor|goto|tp|return|giverank> ...", AdminNotifyType.Warn );
				break;
		}
	}

	public bool AcceptConnection( Connection channel, string reason )
	{
		if ( channel is null )
			return true;

		return !IsBanned( channel.SteamId.Value, out _ );
	}

	public void OnConnected( Connection channel )
	{
		if ( channel is null )
			return;

		if ( IsBanned( channel.SteamId.Value, out var ban ) )
			CloseBannedClient( channel, ban.Reason );
	}

	public void OnActive( Connection channel )
	{
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
	}

	public void OnDisconnected( Connection channel )
	{
	}

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
			NotifyCaller( caller, "Player is not online.", AdminNotifyType.Error );
			return;
		}

		if ( targetConnection.IsHost )
		{
			NotifyCaller( caller, "Host connection cannot be kicked.", AdminNotifyType.Error );
			return;
		}

		var normalizedReason = NormalizeReason( reason, "Kicked by admin" );
		if ( !TryKickConnection( targetConnection, normalizedReason, out var kickError ) )
		{
			NotifyCaller( caller, kickError, AdminNotifyType.Error );
			return;
		}

		NotifyCaller( caller, $"Kicked {targetConnection.DisplayName}: {normalizedReason}", AdminNotifyType.Info );
	}

	private static void Ban( Connection caller, long steamId, string reason )
	{
		if ( !HasAccess( caller, AdministratorRank, out var error ) )
		{
			NotifyCaller( caller, error, AdminNotifyType.Error );
			return;
		}

		var normalizedReason = NormalizeReason( reason, "Banned by admin" );
		var targetConnection = FindConnectionBySteamId( steamId );
		if ( targetConnection is not null && targetConnection.IsHost )
		{
			NotifyCaller( caller, "Host connection cannot be banned.", AdminNotifyType.Error );
			return;
		}

		// TODO: Add ban duration support. For now every ban is permanent.
		SaveBan( new AdminBanRecord
		{
			SteamId = steamId,
			Reason = normalizedReason,
			BannedAt = DateTimeOffset.UtcNow,
			BannedBySteamId = caller is null ? 0 : caller.SteamId.Value,
			BannedByName = caller?.DisplayName ?? "Server",
			IsPermanent = true
		} );

		if ( targetConnection is not null )
			CloseBannedClient( targetConnection, normalizedReason );

		NotifyCaller( caller, $"Banned {steamId}: {normalizedReason}", AdminNotifyType.Info );
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
			NotifyCaller( caller, "SteamId is not banned.", AdminNotifyType.Warn );
			return;
		}

		FileSystem.Data.DeleteFile( path );
		NotifyCaller( caller, $"Unbanned {steamId}.", AdminNotifyType.Info );
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
			NotifyCaller( caller, "Player is not online.", AdminNotifyType.Error );
			return;
		}

		target.HostTriggerRespawn();
		NotifyCaller( caller, $"Respawned {GetPlayerName( target )}.", AdminNotifyType.Info );
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
			NotifyCaller( caller, "Player is not online.", AdminNotifyType.Error );
			return;
		}

		target.Money = Math.Max( 0, value );
		NotifyCaller( caller, $"Set {GetPlayerName( target )} money to ${target.Money}.", AdminNotifyType.Info );
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
			NotifyCaller( caller, "Player is not online.", AdminNotifyType.Error );
			return;
		}

		target.Health = Math.Clamp( value, 0f, target.MaxHealth );
		target.WorldHud?.WorldHudRefresh();
		NotifyCaller( caller, $"Set {GetPlayerName( target )} hp to {target.Health:0}.", AdminNotifyType.Info );
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
			NotifyCaller( caller, "Job not found.", AdminNotifyType.Error );
			return;
		}

		var target = FindPlayerBySteamId( steamId );
		if ( !target.IsValid() )
		{
			NotifyCaller( caller, "Player is not online.", AdminNotifyType.Error );
			return;
		}

		target.Job?.SetJob( normalizedJobId );
		NotifyCaller( caller, $"Set {GetPlayerName( target )} job to {normalizedJobId}.", AdminNotifyType.Info );
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
			NotifyCaller( caller, "This command requires an in-game admin player.", AdminNotifyType.Error );
			return;
		}

		var admin = FindPlayerBySteamId( caller.SteamId.Value );
		if ( !admin.IsValid() || !admin.Controller.IsValid() )
		{
			NotifyCaller( caller, "Admin player is not online.", AdminNotifyType.Error );
			return;
		}

		var scene = Game.ActiveScene;
		if ( scene is null )
		{
			NotifyCaller( caller, "Active scene not found.", AdminNotifyType.Error );
			return;
		}

		var eye = admin.Controller.EyeTransform;
		var traceOrigin = requestedEyePosition ?? eye.Position;
		var traceForward = requestedEyeForward.HasValue && requestedEyeForward.Value.LengthSquared > 0.001f
			? requestedEyeForward.Value.Normal
			: eye.Forward;

		if ( requestedEyePosition.HasValue && Vector3.DistanceBetween( admin.WorldPosition, traceOrigin ) > 200f )
		{
			NotifyCaller( caller, "Door trace origin is too far from you.", AdminNotifyType.Error );
			return;
		}

		var trace = scene.Trace
			.Ray( traceOrigin, traceOrigin + traceForward * DoorTraceDistance )
			.IgnoreGameObjectHierarchy( admin.GameObject )
			.Run();

		var door = FindDoorFromTrace( trace );
		if ( !door.IsValid() )
		{
			NotifyCaller( caller, "Look at a door first.", AdminNotifyType.Error );
			return;
		}

		if ( door.IsBlocked || door.HasOnlyJobs || door.LockState == Door.DoorLockState.Locked || !door.HasOwner )
		{
			NotifyCaller( caller, "This door cannot be force-sold.", AdminNotifyType.Error );
			return;
		}

		door.Sell();
		NotifyCaller( caller, "Door was force-sold.", AdminNotifyType.Info );
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
			NotifyCaller( caller, "This command requires an in-game admin player.", AdminNotifyType.Error );
			return;
		}

		var admin = FindPlayerBySteamId( caller.SteamId.Value );
		var target = FindPlayerBySteamId( steamId );
		if ( !admin.IsValid() || !target.IsValid() )
		{
			NotifyCaller( caller, "Player is not online.", AdminNotifyType.Error );
			return;
		}

		SaveReturnTransform( admin );
		var gotoPos = target.WorldPosition + target.WorldRotation.Backward * 64f;
		var gotoRot = Rotation.LookAt( target.WorldPosition - gotoPos );
		admin.HostTeleport( gotoPos, gotoRot );
		NotifyCaller( caller, $"Teleported to {GetPlayerName( target )}.", AdminNotifyType.Info );
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
			NotifyCaller( caller, "This command requires an in-game admin player.", AdminNotifyType.Error );
			return;
		}

		var admin = FindPlayerBySteamId( caller.SteamId.Value );
		var target = FindPlayerBySteamId( steamId );
		if ( !admin.IsValid() || !target.IsValid() )
		{
			NotifyCaller( caller, "Player is not online.", AdminNotifyType.Error );
			return;
		}

		SaveReturnTransform( target );
		var tpPos = admin.WorldPosition + admin.WorldRotation.Forward * 64f;
		var tpRot = Rotation.LookAt( admin.WorldPosition - tpPos );
		target.HostTeleport( tpPos, tpRot );
		NotifyCaller( caller, $"Teleported {GetPlayerName( target )} to you.", AdminNotifyType.Info );
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
			NotifyCaller( caller, "Player is not online.", AdminNotifyType.Error );
			return;
		}

		if ( !ReturnTransforms.TryGetValue( steamId, out var transform ) )
		{
			NotifyCaller( caller, "No saved return position for this player.", AdminNotifyType.Warn );
			return;
		}

		target.HostTeleport( transform.Position, transform.Rotation );
		ReturnTransforms.Remove( steamId );
		NotifyCaller( caller, $"Returned {GetPlayerName( target )}.", AdminNotifyType.Info );
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

		NotifyCaller( caller, $"Set {steamId} rank to {GetRankName( clampedRank )}.", AdminNotifyType.Info );
	}

	private static bool HasAccess( Connection caller, int requiredRank, out string error )
	{
		error = null;

		if ( IsServerAuthority( caller ) )
			return true;

		if ( IsBanned( caller.SteamId.Value, out var ban ) )
		{
			error = $"You are banned: {ban.Reason}";
			return false;
		}

		var player = FindPlayerBySteamId( caller.SteamId.Value );
		var rank = player.IsValid() ? player.AdminRank : LoadRank( caller.SteamId.Value ).Rank;
		if ( rank >= requiredRank )
			return true;

		error = $"Access denied. Required rank: {GetRankName( requiredRank )}.";
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
		return player.GameObject.Network.Owner?.DisplayName ?? "Player";
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
			RpcCloseGame( NormalizeReason( reason, "Banned from server" ) );
		}

		TryKickConnection( connection, NormalizeReason( reason, "Banned from server" ), out _ );
	}

	private static bool TryKickConnection( Connection connection, string reason, out string error )
	{
		error = null;

		if ( connection is null )
		{
			error = "Player connection is not available.";
			return false;
		}

		if ( connection.IsHost )
		{
			error = "Host connection cannot be kicked.";
			return false;
		}

		try
		{
			connection.Kick( reason );
			return true;
		}
		catch ( Exception ex )
		{
			error = $"Failed to kick connection: {ex.Message}";
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

		using ( Rpc.FilterInclude( c => c.SteamId.Value == caller.SteamId.Value ) )
		{
			RpcNotify( text, type );
		}
	}

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
	private static void RpcCloseGame( string reason )
	{
		Notification.Error( $"Banned: {reason}", 5f );
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
