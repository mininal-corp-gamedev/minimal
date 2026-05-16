using Sandbox;

public sealed class PropCustom : Component, Component.INetworkListener
{
	[Sync( SyncFlags.FromHost )] public Player PlayerOwner { get; private set; }
	[Sync( SyncFlags.FromHost )] public Color PropTint { get; private set; } = Color.White;
	public TriggerBuilding TriggerBuilding { get; private set; }
	private bool _registeredLocally;
	private bool _tintApplied;
	private Color _lastAppliedTint;

	public void SetOwner( Player owner )
	{
		if ( !Networking.IsHost )
			return;

		PlayerOwner = owner;
	}

	public void SetTint( Color tint )
	{
		if ( !Networking.IsHost )
			return;

		PropTint = tint;
		ApplyTint();
	}

	public void SetTriggerBuilding( TriggerBuilding building )
	{
		if ( !Networking.IsHost )
			return;

		TriggerBuilding = building;
	}

	public void ClearTriggerBuilding( TriggerBuilding building )
	{
		if ( !Networking.IsHost )
			return;
		if ( TriggerBuilding != building )
			return;

		TriggerBuilding = null;
	}

	protected override void OnUpdate()
	{
		if ( !_tintApplied || _lastAppliedTint != PropTint )
			ApplyTint();

		if ( _registeredLocally )
			return;
		if ( !PlayerOwner.IsValid() || PlayerOwner.IsProxy )
			return;

		PlayerOwner.RegisterSpawnedProp( this );
		_registeredLocally = true;
	}

	protected override void OnDestroy()
	{
		PlayerOwner?.UnregisterSpawnedProp( this );
	}

	private void ApplyTint()
	{
		foreach ( var renderer in GameObject.Components.GetAll<ModelRenderer>( FindMode.EverythingInSelfAndDescendants ) )
		{
			if ( renderer.IsValid() )
				renderer.Tint = PropTint;
		}

		_lastAppliedTint = PropTint;
		_tintApplied = true;
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
