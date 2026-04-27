using Sandbox;

public sealed class PropCustom : Component, Component.INetworkListener
{
	[Sync( SyncFlags.FromHost )] public Player PlayerOwner { get; private set; }

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

		var networkOwner = GameObject.Network.Owner;
		var playerOwner = PlayerOwner.IsValid() ? PlayerOwner.GameObject.Network.Owner : null;
		if ( networkOwner?.SteamId != channel.SteamId && playerOwner?.SteamId != channel.SteamId )
			return;

		GameObject.Destroy();
	}
}
