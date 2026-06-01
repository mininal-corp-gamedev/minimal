using Sandbox;

namespace Sandbox;

/// <summary>RPC entry points for <see cref="TabMenu"/> (shared client + server).</summary>
public sealed partial class TabMenu
{
	[Rpc.Host]
	private void RpcRequestJoinJob( string jobId )
	{
#if SERVER
		RpcRequestJoinJobServer( jobId );
#endif
	}

	[Rpc.Broadcast]
	private static void RpcReceiveJoinJobResult( string text, bool success )
	{
		if ( success )
			Notification.Info( text, 3.5f );
		else
			Notification.Error( text, 3.5f );
	}
}
