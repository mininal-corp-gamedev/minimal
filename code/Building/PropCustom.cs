using Sandbox;

public sealed class PropCustom : Component, Component.INetworkListener
{
	[Sync( SyncFlags.FromHost )] public Player PlayerOwner { get; private set; }
	[Sync( SyncFlags.FromHost )] public Color PropTint { get; private set; } = Color.White;
	[Sync( SyncFlags.FromHost ), Change( nameof( OnNoCollidePlayersChanged ) )] public bool NoCollidePlayers { get; private set; }
	[Sync( SyncFlags.FromHost ), Change( nameof( OnHasFadingDoorChanged ) )] public bool HasFadingDoor { get; private set; }
	[Sync( SyncFlags.FromHost ), Change( nameof( OnFadingDoorIsOpenChanged ) )] public bool FadingDoorIsOpen { get; private set; }
	public TriggerBuilding TriggerBuilding { get; private set; }
	private bool _registeredLocally;
	private bool _tintApplied;
	private Color _lastAppliedTint;
	private bool _physicsFrozen;
	private bool _freezeRequested;
	private bool _physicsReadyForFreeze;
	private int _physicsReadyFixedTicks;
	private bool _initialPhysicsPending;
	private int _initialPhysicsWaitTicks;
	private PropPhysicsMode _initialPhysicsMode = PropPhysicsMode.Dynamic;
	private float _initialMassOverride;

	private const int FreezeDelayFixedTicks = 1;
	private const int InitialPhysicsMaxWaitTicks = 100;

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
		HostSetFadingDoorOpen( false );
		TryFreezePhysics( force: true );
#endif
	}

	public void DisableFadingDoor()
	{
#if SERVER
		if ( !Networking.IsHost )
			return;

		HostSetFadingDoorOpen( false );
		HasFadingDoor = false;
#endif
	}

	public void HostSetFadingDoorOpen( bool isOpen )
	{
#if SERVER
		if ( !Networking.IsHost || !HasFadingDoor )
			return;

		FadingDoorIsOpen = isOpen;
#endif
	}

	public bool HostToggleFadingDoor( Player player )
	{
#if SERVER
		if ( !Networking.IsHost || !HasFadingDoor )
			return FadingDoorIsOpen;
		if ( !player.IsValid() || PlayerOwner != player )
			return FadingDoorIsOpen;

		HostSetFadingDoorOpen( !FadingDoorIsOpen );
		return FadingDoorIsOpen;
#else
		return false;
#endif
	}

	public void NotifyPhysgunGrabbed()
	{
#if SERVER
		if ( !Networking.IsHost )
			return;

		_physicsFrozen = false;
		_freezeRequested = false;
		_physicsReadyForFreeze = false;
		_physicsReadyFixedTicks = 0;
#endif
	}

	public void ConfigureInitialPhysics( PropPhysicsMode mode, float massOverride = 0f )
	{
#if SERVER
		if ( !Networking.IsHost )
			return;

		_initialPhysicsMode = mode;
		_initialMassOverride = MathF.Max( 0f, massOverride );
		_initialPhysicsPending = true;
		_initialPhysicsWaitTicks = 0;
		_physicsFrozen = false;
		_freezeRequested = false;
		TryApplyInitialPhysics();
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

			PropCollisionTags.ApplyFadingDoorOpenState( GameObject, FadingDoorIsOpen );
			return;
		}

		if ( !GameObject.Components.TryGet<FadingDoor>( out var door ) || !door.IsValid() )
			return;

		PropCollisionTags.ApplyFadingDoorOpenState( GameObject, false );
		_fadingDoorCollisionApplied = false;
		door.Destroy();
	}

	private void OnFadingDoorIsOpenChanged( bool oldValue, bool newValue )
	{
		if ( !HasFadingDoor )
			return;

		PropCollisionTags.ApplyFadingDoorOpenState( GameObject, newValue );

#if SERVER
		if ( !Networking.IsHost )
			return;

		if ( newValue )
			return;

		TryFreezePhysics( force: true );
#endif
	}

	protected override void OnFixedUpdate()
	{
#if SERVER
		TryApplyInitialPhysics();

		if ( _freezeRequested )
			ProcessFreezeRequest( force: false, fromFixedUpdate: true );
#endif
	}

	private bool _fadingDoorCollisionApplied;
	private bool _lastAppliedFadingDoorOpen;

	protected override void OnUpdate()
	{
		if ( HasFadingDoor && ( !_fadingDoorCollisionApplied || _lastAppliedFadingDoorOpen != FadingDoorIsOpen ) )
		{
			PropCollisionTags.ApplyFadingDoorOpenState( GameObject, FadingDoorIsOpen );
			_lastAppliedFadingDoorOpen = FadingDoorIsOpen;
			_fadingDoorCollisionApplied = true;
		}

		if ( !_tintApplied || _lastAppliedTint != PropTint )
			ApplyTint();

		if ( _registeredLocally )
			return;
		if ( !PlayerOwner.IsValid() || PlayerOwner.IsProxy )
			return;

		PlayerOwner.RegisterSpawnedProp( this );
		_registeredLocally = true;
	}

	public bool TryFreezePhysics( bool force = false, bool fromFixedUpdate = false )
	{
#if SERVER
		if ( !Networking.IsHost )
			return false;

		_freezeRequested = true;
		return ProcessFreezeRequest( force, fromFixedUpdate );
#else
		return false;
#endif
	}

#if SERVER
	private bool ProcessFreezeRequest( bool force, bool fromFixedUpdate )
	{

		if ( HasFadingDoor && FadingDoorIsOpen )
			return false;

		if ( GameObject.Tags.Has( PropCollisionTags.PhysgunHeldTag ) && !force )
			return false;

		if ( _physicsFrozen && !force )
		{
			_freezeRequested = false;
			return true;
		}

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
	}

	private void TryApplyInitialPhysics()
	{
		if ( !_initialPhysicsPending || !Networking.IsHost )
			return;

		var rb = GameObject.Components.Get<Rigidbody>( FindMode.EverythingInSelfAndDescendants );
		if ( !rb.IsValid() || rb.IsProxy || rb.PhysicsBody is null || !rb.PhysicsBody.IsValid() )
		{
			_initialPhysicsWaitTicks++;
			if ( _initialPhysicsWaitTicks >= InitialPhysicsMaxWaitTicks )
			{
				_initialPhysicsPending = false;
				Log.Warning( $"[PropCustom] Rigidbody was not ready for '{GameObject.Name}'." );
			}
			return;
		}

		rb.MassOverride = _initialMassOverride;
		PropCollisionTags.RefreshPhysicsShapeTags( GameObject );
		_initialPhysicsPending = false;

		switch ( _initialPhysicsMode )
		{
			case PropPhysicsMode.Dynamic:
				rb.Gravity = true;
				rb.MotionEnabled = true;
				_physicsFrozen = false;
				break;

			case PropPhysicsMode.Frozen:
				_freezeRequested = true;
				ProcessFreezeRequest( force: true, fromFixedUpdate: false );
				break;

			case PropPhysicsMode.Disabled:
				rb.Enabled = false;
				_physicsFrozen = true;
				break;
		}
	}

	private bool FreezeReadyBody( Rigidbody rb )
	{
		if ( HasFadingDoor && FadingDoorIsOpen )
			return false;

		PropCollisionTags.RefreshPhysicsShapeTags( GameObject );
		rb.Velocity = Vector3.Zero;
		rb.AngularVelocity = Vector3.Zero;
		rb.MotionEnabled = false;
		_physicsFrozen = true;
		_freezeRequested = false;
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
