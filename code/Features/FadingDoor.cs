using Sandbox;

public sealed class FadingDoor : Component
{
	[Sync( SyncFlags.FromHost )] public Player PlayerOwner { get; private set; }
	[Sync( SyncFlags.FromHost )] public bool IsOpen { get; private set; }

	private bool _collisionApplied;
	private bool _lastAppliedOpen;

	public void SetOwner( Player owner )
	{
		if ( !Networking.IsHost )
			return;

		PlayerOwner = owner;
	}

	public void Open()
	{
		if ( !Networking.IsHost )
			return;

		IsOpen = true;
		ApplyCollisionState();
	}

	public void Close()
	{
		if ( !Networking.IsHost )
			return;

		IsOpen = false;
		ApplyCollisionState();
	}

	protected override void OnUpdate()
	{
		if ( !_collisionApplied || _lastAppliedOpen != IsOpen )
			ApplyCollisionState();
	}

	protected override void OnFixedUpdate()
	{
		if ( Player.Local != PlayerOwner )
			return;
		if ( !Input.Pressed( "FadingDoorOpenClose" ) )
			return;

		if ( Networking.IsHost )
		{
			HostToggle( Player.Local );
			return;
		}

		RpcRequestToggle();
	}

	[Rpc.Host]
	private void RpcRequestToggle()
	{
		if ( !Networking.IsHost )
			return;

		var caller = Rpc.Caller;
		if ( caller is null )
			return;

		var player = Player.FindPlayerBySteamId( caller.SteamId.Value );
		HostToggle( player );
	}

	private void HostToggle( Player player )
	{
		if ( !Networking.IsHost )
			return;
		if ( !player.IsValid() || player != PlayerOwner )
			return;

		if ( IsOpen )
			Close();
		else
			Open();
	}

	private void ApplyCollisionState()
	{
		foreach ( var collider in GameObject.Components.GetAll<Collider>( FindMode.EverythingInSelfAndDescendants ) )
		{
			if ( collider.IsValid() )
				collider.Enabled = !IsOpen;
		}

		_lastAppliedOpen = IsOpen;
		_collisionApplied = true;
	}
}
