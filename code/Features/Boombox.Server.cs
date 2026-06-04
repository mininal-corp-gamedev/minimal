using Sandbox;
using System;

public sealed partial class Boombox
{
	[Rpc.Broadcast( NetFlags.HostOnly )]
	private void RpcSyncPlaybackState( bool isEnabled )
	{
		if ( isEnabled )
			StartRadioPlayback();
		else
			StopRadioPlayback();
	}

	public static void HostSyncAllToConnection( Connection connection )
	{
#if SERVER
		if ( !Networking.IsHost || connection is null )
			return;

		var scene = Game.ActiveScene;
		if ( scene is null )
			return;

		foreach ( var boombox in scene.GetAllComponents<Boombox>() )
		{
			if ( !boombox.IsValid() )
				continue;

			boombox.HostSyncPlaybackToConnection( connection );
		}
#endif
	}

	private void HostSyncPlaybackToConnection( Connection connection )
	{
#if SERVER
		if ( !Networking.IsHost || connection is null )
			return;

		using ( Rpc.FilterInclude( c => c.SteamId.Value == connection.SteamId.Value ) )
		{
			RpcSyncPlaybackState( IsRadioEnabled );
		}
#endif
	}

	private void ToggleRadioServer()
	{
#if SERVER
		if ( !Networking.IsHost )
			return;

		IsRadioEnabled = !IsRadioEnabled;
#endif
	}

	private void ApplyDamageServer( float damage )
	{
#if SERVER
		if ( damage <= 0f || Health <= 0f )
			return;

		Health = MathF.Max( 0f, Health - damage );

		if ( Health > 0f )
			return;

		IsRadioEnabled = false;
		GameObject.Destroy();
#endif
	}

	private bool TryGetCallerBoomboxContext( GameObject boomboxObject, out Boombox boombox, out Player player, float extraDistance )
	{
		boombox = null;
		player = null;

#if SERVER
		if ( !Networking.IsHost )
			return false;

		var caller = Rpc.Caller;
		if ( caller is null )
			return false;

		if ( boomboxObject != GameObject )
			return false;

		boombox = boomboxObject.Components.Get<Boombox>();
		if ( !boombox.IsValid() )
			return false;

		player = Player.FindPlayerBySteamId( caller.SteamId.Value );
		if ( !player.IsValid() || player.GameObject.Network.Owner != caller )
			return false;

		if ( Vector3.DistanceBetween( player.WorldPosition, boombox.WorldPosition ) > MathF.Max( 1f, boombox.MaxUseDistance + extraDistance ) )
			return false;

		return true;
#else
		return false;
#endif
	}
}
