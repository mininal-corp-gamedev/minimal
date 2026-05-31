using Sandbox;

public sealed class FadingDoor : Component, IDoorHackable
{
	[Sync( SyncFlags.FromHost )] public Player PlayerOwner { get; private set; }
	[Sync( SyncFlags.FromHost )] public bool IsOpen { get; private set; }

	[Property, Category( "Lockpick" )] public float LockpickInteractRange { get; set; } = 120f;

	public string DoorHackName => "Fading Door";
	public Vector3 DoorHackWorldPosition => WorldPosition;
	public float DoorHackInteractRange => LockpickInteractRange;
	private Player EffectiveOwner
	{
		get
		{
			if ( PlayerOwner.IsValid() )
				return PlayerOwner;

			var prop = GameObject.Components.Get<PropCustom>( FindMode.EverythingInSelfAndAncestors );
			return prop.IsValid() ? prop.PlayerOwner : null;
		}
	}

	private bool _collisionApplied;
	private bool _lastAppliedOpen;

	public void SetOwner( Player owner )
	{
#if SERVER
		if ( !Networking.IsHost )
			return;

		PlayerOwner = owner;
#endif
	}

	public void Open()
	{
#if SERVER
		if ( !Networking.IsHost )
			return;

		IsOpen = true;
		ApplyCollisionState();
#endif
	}

	public void Close()
	{
#if SERVER
		if ( !Networking.IsHost )
			return;

		IsOpen = false;
		ApplyCollisionState();
#endif
	}

	protected override void OnUpdate()
	{
		if ( !_collisionApplied || _lastAppliedOpen != IsOpen )
			ApplyCollisionState();
	}

	protected override void OnFixedUpdate()
	{
		if ( Player.Local != EffectiveOwner )
			return;
		if ( !Input.Pressed( "FadingDoorOpenClose" ) )
			return;

		if ( Networking.IsHost )
		{
#if SERVER
			HostToggle( Player.Local );
#endif
			return;
		}

		RpcRequestToggle();
	}

	[Rpc.Host]
	private void RpcRequestToggle()
	{
#if SERVER
		if ( !Networking.IsHost )
			return;

		var caller = Rpc.Caller;
		if ( caller is null )
			return;

		var player = Player.FindPlayerBySteamId( caller.SteamId.Value );
		HostToggle( player );
#endif
	}

#if SERVER
	private void HostToggle( Player player )
	{
		if ( !Networking.IsHost )
			return;
		if ( !player.IsValid() || player != EffectiveOwner )
			return;

		if ( IsOpen )
			Close();
		else
			Open();
	}
#endif

	[Rpc.Host]
	public void RpcRequestLockpick()
	{
#if SERVER
		DoorHackSystem.HostRequestStartHackFromRpc( GameObject );
#endif
	}

	public void RequestDoorHack()
	{
		RpcRequestLockpick();
	}

	public bool CanBeDoorHacked( Player hacker )
	{
		if ( IsOpen ) return false;
		var owner = EffectiveOwner;
		if ( !owner.IsValid() ) return false;
		if ( hacker.IsValid() && hacker.IsArrested ) return false;
		return true;
	}

	public void HostOnDoorHackSucceeded( Player hacker )
	{
#if SERVER
		if ( !Networking.IsHost ) return;
		if ( !CanBeDoorHacked( hacker ) ) return;

		Open();
#endif
	}

	public void HostOnDoorHackFailed( Player hacker )
	{
#if SERVER
		if ( !Networking.IsHost ) return;
#endif
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
