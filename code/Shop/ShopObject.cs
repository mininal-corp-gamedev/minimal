using Sandbox;
using Minimal.Shop;

public sealed class ShopObject : Component, Component.INetworkListener
{
	[Sync( SyncFlags.FromHost )] public Player PlayerOwner { get; private set; }
	[Sync( SyncFlags.FromHost )] public ShopDefinition Definition { get; set; }

	public void SetOwner( Player owner )
	{
		if ( !Networking.IsHost )
			return;

		PlayerOwner = owner;
	}

	void Component.INetworkListener.OnDisconnected( Connection channel )
	{
		if ( !Networking.IsHost )
			return;

		if ( !IsPlayerOwnerConnection( channel ) )
			return;

		GameObject.Destroy();
	}

	private bool IsPlayerOwnerConnection( Connection channel )
	{
		if ( !PlayerOwner.IsValid() || channel is null )
			return false;

		var ownerConn = PlayerOwner.GameObject.Network.Owner;
		return ownerConn is not null && ownerConn.SteamId == channel.SteamId;
	}

	public static void HostDestroyAllForPlayerOwner( Player playerOwner )
	{
		if ( !Networking.IsHost || !playerOwner.IsValid() )
			return;

		var scene = Game.ActiveScene;
		if ( scene is null )
			return;

		foreach ( var so in scene.GetAllComponents<ShopObject>() )
		{
			if ( !so.IsValid() || !so.GameObject.IsValid() )
				continue;
			if ( so.PlayerOwner != playerOwner )
				continue;

			so.GameObject.Destroy();
		}
	}
}
