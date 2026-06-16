using System;
using System.Collections.Generic;
using System.Text.Json;
using Sandbox;

namespace Minimal.Clan;

public sealed partial class ClanManager : Component
{
	public const int CreatePrice = 5000;
	public const float InviteCooldownSeconds = 10f;
	public const int AdminDeleteRank = 2;

	public static ClanManager Instance { get; private set; }

	[Sync( SyncFlags.FromHost )] public string ClanSummariesJson { get; private set; } = "[]";
	[Sync( SyncFlags.FromHost )] public int ClanVersion { get; private set; }

	public int PendingInviteClanId { get; private set; } = -1;
	public string PendingInviteClanHeader { get; private set; } = "";
	public long PendingInviteInviterSteamId { get; private set; }
	public string PendingInviteInviterName { get; private set; } = "";
	public int PendingInviteVersion { get; private set; }

	protected override void OnAwake()
	{
		if ( Instance is null )
			Instance = this;

#if SERVER
		HostLoadClans();
#endif
	}

	protected override void OnDestroy()
	{
		if ( Instance == this )
			Instance = null;
	}

	protected override void OnUpdate()
	{
#if SERVER
		HostUpdateRuntimeSync();
#endif
	}

	public IReadOnlyList<ClanSummary> GetClanSummaries()
	{
		try
		{
			return JsonSerializer.Deserialize<List<ClanSummary>>( ClanSummariesJson ?? "[]" ) ?? new List<ClanSummary>();
		}
		catch ( Exception e )
		{
			Log.Warning( $"[Clan] Failed to parse summaries: {e.Message}" );
			return new List<ClanSummary>();
		}
	}

	public ClanSummary GetClanSummary( int clanId )
	{
		foreach ( var clan in GetClanSummaries() )
		{
			if ( clan.Id == clanId )
				return clan;
		}

		return null;
	}

	[Rpc.Host]
	public static void RpcRequestCreateClan( string header, string description, string colorId, string iconPath, string backgroundType, string backgroundColor1, string backgroundColor2, string backgroundColor3, string backgroundColor4 )
	{
#if SERVER
		Instance?.HostCreateClan( header, description, colorId, iconPath, backgroundType, backgroundColor1, backgroundColor2, backgroundColor3, backgroundColor4 );
#endif
	}

	[Rpc.Host]
	public static void RpcRequestUpdateClanSettings( string description, string colorId, string iconPath, string backgroundType, string backgroundColor1, string backgroundColor2, string backgroundColor3, string backgroundColor4 )
	{
#if SERVER
		Instance?.HostUpdateClanSettings( description, colorId, iconPath, backgroundType, backgroundColor1, backgroundColor2, backgroundColor3, backgroundColor4 );
#endif
	}

	[Rpc.Host]
	public static void RpcRequestDeleteClan()
	{
#if SERVER
		Instance?.HostDeleteOwnClan();
#endif
	}

	[Rpc.Host]
	public static void RpcRequestLeaveClan()
	{
#if SERVER
		Instance?.HostLeaveClan();
#endif
	}

	[Rpc.Host]
	public static void RpcRequestInvite( long targetSteamId )
	{
#if SERVER
		Instance?.HostInvite( targetSteamId );
#endif
	}

	[Rpc.Host]
	public static void RpcRequestAcceptInvite()
	{
#if SERVER
		Instance?.HostAcceptInvite();
#endif
	}

	[Rpc.Host]
	public static void RpcRequestDeclineInvite()
	{
#if SERVER
		Instance?.HostDeclineInvite();
#endif
	}

	[Rpc.Host]
	public static void RpcRequestKickMember( long targetSteamId )
	{
#if SERVER
		Instance?.HostKickMember( targetSteamId );
#endif
	}

	[Rpc.Host]
	public static void RpcRequestSetMemberRank( long targetSteamId, int rankValue )
	{
#if SERVER
		Instance?.HostSetMemberRank( targetSteamId, rankValue );
#endif
	}

	public static bool CanInviteRank( ClanRank rank ) => rank is ClanRank.Leader or ClanRank.DeputyLeader or ClanRank.Officer;
	public static bool CanEditSettingsRank( ClanRank rank ) => rank is ClanRank.Leader or ClanRank.DeputyLeader;

	private void SetClanSummariesJson( string json )
	{
#if SERVER
		ClanSummariesJson = string.IsNullOrWhiteSpace( json ) ? "[]" : json;
		ClanVersion++;
#endif
	}

	private void ClearPendingInvite()
	{
		PendingInviteClanId = -1;
		PendingInviteClanHeader = "";
		PendingInviteInviterSteamId = 0L;
		PendingInviteInviterName = "";
		PendingInviteVersion++;
	}

	[Rpc.Broadcast]
	private static void RpcReceiveClanSummaries( string summariesJson, int version )
	{
		if ( Instance is null )
			return;

		Instance.ClanSummariesJson = string.IsNullOrWhiteSpace( summariesJson ) ? "[]" : summariesJson;
		Instance.ClanVersion = version;
	}

	[Rpc.Broadcast]
	private static void RpcReceiveInvite( int clanId, string clanHeader, long inviterSteamId, string inviterName )
	{
		if ( Instance is null )
			return;

		Instance.PendingInviteClanId = clanId;
		Instance.PendingInviteClanHeader = clanHeader ?? "";
		Instance.PendingInviteInviterSteamId = inviterSteamId;
		Instance.PendingInviteInviterName = inviterName ?? "";
		Instance.PendingInviteVersion++;
	}

	[Rpc.Broadcast]
	private static void RpcClearInvite( int clanId )
	{
		if ( Instance is null )
			return;

		if ( clanId >= 0 && Instance.PendingInviteClanId != clanId )
			return;

		Instance.ClearPendingInvite();
	}

	[Rpc.Broadcast]
	private static void RpcReceiveClanResult( string message, bool success )
	{
		if ( string.IsNullOrWhiteSpace( message ) )
			return;

		if ( success )
			Notification.Info( message, 3.5f );
		else
			Notification.Error( message, 3.5f );
	}
}
