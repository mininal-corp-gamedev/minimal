using Sandbox;

namespace Sandbox;

public sealed partial class DemotePanel
{
	[Rpc.Host]
	private static void RpcRequestDemote( long targetSteamId )
	{
#if SERVER
		RpcRequestDemoteServer( targetSteamId );
#endif
	}

	[Rpc.Broadcast]
	private static void RpcReceiveDemoteResult( string text, bool success )
	{
		if ( success )
			Notification.Info( text, 3.5f );
		else
			Notification.Error( text, 3.5f );
	}
}
