using Sandbox;

public sealed class PropCustom : Component, Component.INetworkListener
{
	[Sync( SyncFlags.FromHost )] public Player PlayerOwner { get; private set; }
	private bool _registeredLocally;

	public void SetOwner( Player owner )
	{
		if ( !Networking.IsHost )
			return;

		PlayerOwner = owner;
	}

	protected override void OnUpdate()
	{
		if ( _registeredLocally )
			return;
		if ( !PlayerOwner.IsValid() || PlayerOwner.IsProxy )
			return;

		PlayerOwner.RegisterSpawnedProp( this );
		_registeredLocally = true;
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
