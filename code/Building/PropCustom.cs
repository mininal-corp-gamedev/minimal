using Sandbox;

public sealed class PropCustom : Component, Component.INetworkListener
{
	[Sync( SyncFlags.FromHost )] public Player PlayerOwner { get; private set; }
	[Sync( SyncFlags.FromHost )] public Color PropTint { get; private set; } = Color.White;
	[Sync( SyncFlags.FromHost ), Change( nameof( OnNoCollidePlayersChanged ) )] public bool NoCollidePlayers { get; private set; }
	[Sync( SyncFlags.FromHost ), Change( nameof( OnHasFadingDoorChanged ) )] public bool HasFadingDoor { get; private set; }
	public TriggerBuilding TriggerBuilding { get; private set; }
	private bool _registeredLocally;
	private bool _tintApplied;
	private Color _lastAppliedTint;
	private bool _physicsFrozen;
	private bool _physicsReadyForFreeze;
	private int _physicsReadyFixedTicks;

	private const int FreezeDelayFixedTicks = 1;

	protected override void OnStart()
	{
		TryFreezePhysics();
	}

	public void SetOwner( Player owner )
	{
#if SERVER
		if ( !Networking.IsHost )
			return;

		PlayerOwner = owner;
#endif
	}

	public void SetTint( Color tint )
	{
#if SERVER
		if ( !Networking.IsHost )
			return;

		PropTint = tint;
		ApplyTint();
#endif
	}

	public void SetTriggerBuilding( TriggerBuilding building )
	{
#if SERVER
		if ( !Networking.IsHost )
			return;

		TriggerBuilding = building;
#endif
	}

	public void ClearTriggerBuilding( TriggerBuilding building )
	{
#if SERVER
		if ( !Networking.IsHost )
			return;
		if ( TriggerBuilding != building )
			return;

		TriggerBuilding = null;
#endif
	}

	public void SetNoCollidePlayers( bool enabled )
	{
#if SERVER
		if ( !Networking.IsHost )
			return;

		NoCollidePlayers = enabled;
#endif
	}

	public void EnableFadingDoor( Player owner )
	{
#if SERVER
		if ( !Networking.IsHost )
			return;
		if ( HasFadingDoor )
			return;

		HasFadingDoor = true;

		if ( !GameObject.Components.TryGet<FadingDoor>( out var door ) )
			door = GameObject.Components.Create<FadingDoor>();

		if ( !door.IsValid() )
			return;

		door.SetOwner( owner );
		door.Close();
#endif
	}

	public void DisableFadingDoor()
	{
#if SERVER
		if ( !Networking.IsHost )
			return;

		HasFadingDoor = false;
#endif
	}

	public void NotifyPhysgunGrabbed()
	{
#if SERVER
		if ( !Networking.IsHost )
			return;

		_physicsFrozen = false;
		_physicsReadyForFreeze = false;
		_physicsReadyFixedTicks = 0;
#endif
	}

	private void OnNoCollidePlayersChanged( bool oldValue, bool newValue )
	{
		PropCollisionTags.ApplyNoCollideTag( GameObject, newValue );
	}

	private void OnHasFadingDoorChanged( bool oldValue, bool newValue )
	{
		if ( newValue )
		{
			if ( !GameObject.Components.TryGet<FadingDoor>( out _ ) )
				GameObject.Components.Create<FadingDoor>();

			return;
		}

		if ( !GameObject.Components.TryGet<FadingDoor>( out var door ) || !door.IsValid() )
			return;

		door.Close();
		door.Destroy();
	}

	protected override void OnFixedUpdate()
	{
		TryFreezePhysics( fromFixedUpdate: true );

		if ( !HasFadingDoor )
			return;
		if ( Player.Local != PlayerOwner )
			return;
		if ( !Input.Pressed( "FadingDoorOpenClose" ) )
			return;

		if ( Networking.IsHost )
		{
#if SERVER
			HostToggleFadingDoor( Player.Local );
#endif
			return;
		}

		RpcRequestFadingDoorToggle();
	}

	protected override void OnUpdate()
	{
		TryFreezePhysics();

		if ( !_tintApplied || _lastAppliedTint != PropTint )
			ApplyTint();

		if ( _registeredLocally )
			return;
		if ( !PlayerOwner.IsValid() || PlayerOwner.IsProxy )
			return;

		PlayerOwner.RegisterSpawnedProp( this );
		_registeredLocally = true;
	}

	[Rpc.Host]
	private void RpcRequestFadingDoorToggle()
	{
#if SERVER
		if ( !Networking.IsHost )
			return;

		var caller = Rpc.Caller;
		if ( caller is null )
			return;

		var player = Player.FindPlayerBySteamId( caller.SteamId.Value );
		HostToggleFadingDoor( player );
#endif
	}

#if SERVER
	private void HostToggleFadingDoor( Player player )
	{
		if ( !Networking.IsHost || !HasFadingDoor )
			return;
		if ( !player.IsValid() || player != PlayerOwner )
			return;
		if ( !GameObject.Components.TryGet<FadingDoor>( out var door ) || !door.IsValid() )
			return;

		if ( door.IsOpen )
			door.Close();
		else
			door.Open();
	}
#endif

	public bool TryFreezePhysics( bool force = false, bool fromFixedUpdate = false )
	{
#if SERVER
		if ( !Networking.IsHost )
			return false;

		if ( GameObject.Tags.Has( PropCollisionTags.PhysgunHeldTag ) && !force )
			return false;

		if ( _physicsFrozen && !force )
			return true;

		var rb = GameObject.Components.Get<Rigidbody>( FindMode.EverythingInSelfAndDescendants );
		if ( !rb.IsValid() || rb.IsProxy )
			return false;
		if ( rb.PhysicsBody is null || !rb.PhysicsBody.IsValid() )
			return false;
		if ( !PropCollisionTags.TryRefreshPhysicsShapeTags( GameObject, out _ ) )
			return false;

		if ( force )
			return FreezeReadyBody( rb );

		if ( !_physicsReadyForFreeze )
		{
			_physicsReadyForFreeze = true;
			_physicsReadyFixedTicks = 0;
			return false;
		}

		if ( fromFixedUpdate )
			_physicsReadyFixedTicks++;

		if ( _physicsReadyFixedTicks < FreezeDelayFixedTicks )
			return false;

		return FreezeReadyBody( rb );
#else
		return false;
#endif
	}

#if SERVER
	private bool FreezeReadyBody( Rigidbody rb )
	{
		PropCollisionTags.RefreshPhysicsShapeTags( GameObject );
		rb.Velocity = Vector3.Zero;
		rb.AngularVelocity = Vector3.Zero;
		rb.MotionEnabled = false;
		_physicsFrozen = true;
		_physicsReadyForFreeze = false;
		_physicsReadyFixedTicks = 0;
		return true;
	}
#endif

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
#if SERVER
		if ( !Networking.IsHost )
			return;

		if ( !IsPlayerOwnerConnection( channel ) )
			return;

		GameObject.Destroy();
#endif
	}

	public static bool IsPlayerOwnerConnection( PropCustom prop, Connection channel )
	{
		if ( !prop.IsValid() || channel is null )
			return false;

		return prop.IsPlayerOwnerConnection( channel );
	}

	private bool IsPlayerOwnerConnection( Connection channel )
	{
		if ( !PlayerOwner.IsValid() )
			return false;

		var ownerConn = PlayerOwner.GameObject.Network.Owner;
		return ownerConn is not null && ownerConn.SteamId == channel.SteamId;
	}

	public static void HostDestroyAllForPlayerOwner( Player playerOwner )
	{
#if SERVER
		if ( !Networking.IsHost || !playerOwner.IsValid() )
			return;

		var scene = Game.ActiveScene;
		if ( scene is null )
			return;

		foreach ( var go in scene.GetAllObjects( true ) )
		{
			if ( !go.Components.TryGet<PropCustom>( out var prop ) )
				continue;
			if ( !prop.IsValid() || !prop.GameObject.IsValid() )
				continue;
			if ( prop.PlayerOwner != playerOwner )
				continue;

			prop.GameObject.Destroy();
		}
#endif
	}
}
