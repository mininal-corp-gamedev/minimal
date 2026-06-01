using Sandbox;
using System;

namespace Sandbox;

public sealed partial class Chat
{
	[Rpc.Host]
	private void SendChatToHost( string message )
	{
#if SERVER
		SendChatToHostServer( message );
#endif
	}

	[Rpc.Broadcast]
	private void BroadcastChatMessage( string senderName, long steamId, string message, ChatMessageType type )
	{
		if ( type == ChatMessageType.System )
		{
			AddMessage( ChatMessage.CreateSystem( message ) );
			return;
		}

		var safeSteamId = steamId > 0 ? (ulong?)steamId : null;
		AddMessage( ChatMessage.CreatePlayer( senderName, safeSteamId, message, type ) );
	}

	public static void SendLocalSystemMessageFromHost( Player origin, string message )
	{
#if SERVER
		SendLocalSystemMessageFromHostServer( origin, message );
#endif
	}

	[Rpc.Broadcast]
	private static void RpcReceiveLocalSystemMessage( string message )
	{
		Instance?.AddLocalSystemMessage( message );
	}
}
