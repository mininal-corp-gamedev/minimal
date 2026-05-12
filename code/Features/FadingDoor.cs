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
		if ( Player.Local != EffectiveOwner )
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
		if ( !player.IsValid() || player != EffectiveOwner )
			return;

		if ( IsOpen )
			Close();
		else
			Open();
	}

	[Rpc.Host]
	public void RpcRequestLockpick()
	{
		DoorHackSystem.HostRequestStartHackFromRpc( GameObject );
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
		if ( !Networking.IsHost ) return;
		if ( !CanBeDoorHacked( hacker ) ) return;

		Open();
	}

	public void HostOnDoorHackFailed( Player hacker )
	{
		if ( !Networking.IsHost ) return;
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
