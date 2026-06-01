using Sandbox;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Sandbox;

public sealed partial class Chat
{
	private readonly Dictionary<ulong, double> HostNextAllowedSendAt = new();

	private void SendChatToHostServer( string message )
	{
		if ( !Networking.IsHost )
			return;

		var caller = Rpc.Caller;
		if ( caller is null )
			return;

		if ( !PassHostValidation( caller, message, out var normalized ) )
			return;

		var sender = FindPlayerBySteamId( caller.SteamId.Value );
		if ( !sender.IsValid() )
			return;

		if ( normalized.StartsWith( "/" ) )
			HandleHostCommand( caller, sender, normalized );
		else
			SendLocalMessage( caller, sender, normalized, ChatMessageType.Local );
	}

	private static void SendLocalSystemMessageFromHostServer( Player origin, string message )
	{
		if ( !Networking.IsHost || !origin.IsValid() )
			return;

		var normalized = NormalizeText( message );
		if ( normalized.Length <= 0 )
			return;

		var maxLength = Math.Max( 1, Instance?.MaxMessageLength ?? 160 );
		if ( normalized.Length > maxLength )
			normalized = normalized[..maxLength];

		var radius = MathF.Max( 1f, Instance?.MessageRadius ?? 400f );
		var recipients = GetAllPlayers()
			.Where( x => x.IsValid() && x.GameObject.Network.Owner is not null )
			.Where( x => Vector3.DistanceBetween( origin.WorldPosition, x.WorldPosition ) <= radius )
			.Select( x => x.GameObject.Network.Owner?.SteamId.Value ?? 0L )
			.Where( x => x > 0L )
			.ToHashSet();

		using ( Rpc.FilterInclude( connection => recipients.Contains( connection.SteamId.Value ) ) )
		{
			RpcReceiveLocalSystemMessage( normalized );
		}
	}

	private void HandleHostCommand( Connection caller, Player sender, string message )
	{
		var commandLine = message[1..].TrimStart();
		if ( commandLine.Length <= 0 )
			return;

		if ( commandLine.StartsWith( "/" ) )
		{
			SendGlobalMessage( caller, commandLine[1..].Trim(), ChatMessageType.Ooc );
			return;
		}

		var parts = commandLine.Split( ' ', 2, StringSplitOptions.RemoveEmptyEntries );
		var command = parts.Length > 0 ? parts[0].ToLowerInvariant() : "";
		var args = parts.Length > 1 ? parts[1].Trim() : "";

		switch ( command )
		{
			case "ooc":
				SendGlobalMessage( caller, args, ChatMessageType.Ooc );
				break;
			case "me":
				if ( args.Length <= 0 )
					return;

				SendLocalMessage( caller, sender, $"{caller.DisplayName} {args}", ChatMessageType.Me );
				break;
			case "roll":
				var roll = Game.Random.Int( 0, 100 );
				SendLocalMessage( caller, sender, $"rolls {roll}/100", ChatMessageType.Roll );
				break;
			case "report":
				SendReportMessage( caller, args );
				break;
			default:
				SendToCaller( caller, GameLocalization.Phrase( "notify.chat.unknown_command", "Unknown command." ), ChatMessageType.System );
				break;
		}
	}

	private void SendGlobalMessage( Connection caller, string message, ChatMessageType type )
	{
		var normalized = NormalizeText( message );
		if ( normalized.Length <= 0 )
			return;

		BroadcastChatMessage( caller.DisplayName, caller.SteamId.Value, normalized, type );
	}

	private void SendLocalMessage( Connection caller, Player sender, string message, ChatMessageType type )
	{
		var normalized = NormalizeText( message );
		if ( normalized.Length <= 0 )
			return;

		var recipients = GetPlayersInMessageRadius( sender )
			.Select( x => x.GameObject.Network.Owner?.SteamId.Value ?? 0L )
			.Where( x => x > 0L )
			.ToHashSet();

		using ( Rpc.FilterInclude( connection => recipients.Contains( connection.SteamId.Value ) ) )
		{
			BroadcastChatMessage( caller.DisplayName, caller.SteamId.Value, normalized, type );
		}
	}

	private void SendReportMessage( Connection caller, string reason )
	{
		var text = GameLocalization.Format( "notify.chat.report_called", "SteamId {0} ({1}) called for help.", caller.SteamId.Value, caller.DisplayName );
		var normalizedReason = NormalizeText( reason );
		if ( normalizedReason.Length > 0 )
			text += GameLocalization.Format( "notify.chat.report_reason", " Reason: {0}", normalizedReason );

		var recipients = GetAllPlayers()
			.Where( x => x.AdminRank > 0 )
			.Select( x => x.GameObject.Network.Owner?.SteamId.Value ?? 0L )
			.Where( x => x > 0L )
			.ToHashSet();

		using ( Rpc.FilterInclude( connection => recipients.Contains( connection.SteamId.Value ) ) )
		{
			BroadcastChatMessage( GameLocalization.Phrase( "ui.chat.report", "Report" ), caller.SteamId.Value, text, ChatMessageType.Report );
		}

		SendToCaller( caller, GameLocalization.Phrase( "notify.chat.report_sent", "Report sent." ), ChatMessageType.System );
	}

	private void SendToCaller( Connection caller, string message, ChatMessageType type )
	{
		using ( Rpc.FilterInclude( connection => connection.SteamId == caller.SteamId ) )
		{
			BroadcastChatMessage( GameLocalization.Phrase( "ui.chat.system", "System" ), 0L, message, type );
		}
	}

	private IEnumerable<Player> GetPlayersInMessageRadius( Player sender )
	{
		var radius = MathF.Max( 1f, MessageRadius );
		foreach ( var player in GetAllPlayers() )
		{
			if ( !player.IsValid() || player.GameObject.Network.Owner is null )
				continue;

			if ( Vector3.DistanceBetween( sender.WorldPosition, player.WorldPosition ) <= radius )
				yield return player;
		}
	}

	private static IEnumerable<Player> GetAllPlayers()
	{
		var scene = Game.ActiveScene;
		if ( scene is null )
			yield break;

		foreach ( var player in scene.GetAllComponents<Player>() )
			yield return player;
	}

	private static Player FindPlayerBySteamId( long steamId )
	{
		foreach ( var player in GetAllPlayers() )
		{
			if ( player.GameObject.Network.Owner?.SteamId.Value == steamId )
				return player;
		}

		return null;
	}

	private bool PassHostValidation( Connection caller, string message, out string normalized )
	{
		normalized = NormalizeText( message );

		if ( normalized.Length <= 0 || normalized.Length > MaxMessageLength )
			return false;

		var steamId = caller.SteamId;
		if ( HostNextAllowedSendAt.TryGetValue( steamId, out var nextAllowedAt ) && Time.Now < nextAllowedAt )
			return false;

		HostNextAllowedSendAt[steamId] = Time.Now + SendCooldownSeconds;
		return true;
	}
}
