using Sandbox;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Sandbox;

/// <summary>RPC entry points for <see cref="PropsMenu"/> (shared client + server).</summary>
public sealed partial class PropsMenu
{
	[Rpc.Host]
	private static void RpcRequestPropFavorites()
	{
#if SERVER
		RpcRequestPropFavoritesServer();
#endif
	}

	[Rpc.Host]
	private static void RpcSetPropFavorite( string propId, bool favorite )
	{
#if SERVER
		RpcSetPropFavoriteServer( propId, favorite );
#endif
	}

	[Rpc.Host]
	private static void RpcRequestSpawnProp( string propId, Vector3 eyePosition, Vector3 eyeForward )
	{
#if SERVER
		RpcRequestSpawnPropServer( propId, eyePosition, eyeForward );
#endif
	}

	[Rpc.Broadcast]
	private static void RpcReceivePropFavorites( string propIdsJson )
	{
		try
		{
			var ids = JsonSerializer.Deserialize<List<string>>( propIdsJson ?? "[]" ) ?? new();
			Instance?.ApplyFavoriteIds( ids );
		}
		catch ( Exception e )
		{
			Log.Warning( $"[PropsMenu] Failed to receive favorites: {e.Message}" );
		}
	}

	[Rpc.Broadcast]
	private static void RpcReceiveSpawnResult( string text, bool success )
	{
		if ( success )
			Notification.Info( text, 3.5f );
		else
			Notification.Error( text, 3.5f );
	}
}
