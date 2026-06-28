using Sandbox;
using System;
using System.Collections.Generic;

/// <summary>A door component with open/close animation, lock state, and break mechanics.</summary>
public sealed class Door : Component, Component.IPressable, Component.INetworkListener, IDoorHackable
{
	public enum DoorState { Closed, Open }
	public enum DoorLockState { Unlocked, Locked }
	public enum DoorMovementMode { Swing, Slide }
	public enum DoorSlideAxis { LocalRight, LocalForward, LocalUp }

	/// <summary>Display name shown on the world HUD when the door has no owner or is blocked.</summary>
	[Property] public string Header { get; set; } = "Door";

	[Property] public DoorWorldHud WorldHud { get; set; }

	/// <summary>If true, the door is admin-blocked: it cannot be bought, sold, locked, or unlocked. Only the header is shown on the HUD.</summary>
	[Property] public bool IsBlocked { get; set; } = false;

	/// <summary>Buy price of the door. Selling refunds half of this value.</summary>
	[Property] public int BuyPrice { get; set; } = 50;

	/// <summary>Sell price — half of <see cref="BuyPrice"/>.</summary>
	public int SellPrice => Math.Max( 0, BuyPrice / 2 );

	/// <summary>If true, the door belongs to specific jobs: it cannot be bought, and only players holding an allowed job can lock/unlock it.</summary>
	[Property] public bool HasOnlyJobs { get; set; } = false;

	/// <summary>Jobs allowed to lock/unlock this door when <see cref="HasOnlyJobs"/> is true.</summary>
	[Property, ShowIf( "HasOnlyJobs", true )] public List<JobDefinition> AllowedJobs { get; set; } = new();

	/// <summary>If true, a job-only door starts locked when the host initializes it.</summary>
	[Property, ShowIf( "HasOnlyJobs", true )] public bool StartLocked { get; set; } = false;

	/// <summary>
	/// Optional paired door. When set, Buy/Sell/Open/Close/Lock/Unlock mirror to the paired door.
	/// Pairing is bidirectional and must be authored manually: set <c>DoorSecond</c> on both doors pointing at each other.
	/// </summary>
	[Property] public Door DoorSecond { get; set; }

	/// <summary>Gameplay owner of this door. Set by Buy, cleared by Sell. Not the network object owner.</summary>
	[Sync( SyncFlags.FromHost )] public Player PlayerOwner { get; private set; }

	/// <summary>True if the door currently has a gameplay owner.</summary>
	public bool HasOwner => PlayerOwner.IsValid();

	/// <summary>True on the local client if the local Player is the gameplay owner of this door.</summary>
	public bool IsLocalPlayerOwner => HasOwner && PlayerOwner == Player.Local;

	/// <summary>Display name of the gameplay owner, or empty string if none.</summary>
	public string OwnerDisplayName => PlayerOwner.IsValid() ? ( PlayerOwner.Network.Owner?.DisplayName ?? "" ) : "";

	/// <summary>SteamId of the gameplay owner, or 0 if none.</summary>
	public long OwnerSteamId => PlayerOwner.IsValid() && PlayerOwner.Network.Owner is not null ? PlayerOwner.Network.Owner.SteamId.Value : 0L;

	// ===== Roommates =====

	/// <summary>Host-authoritative, network-synced, semicolon-separated list of roommate SteamIds.</summary>
	[Sync( SyncFlags.FromHost )] public string RoommateIdsSerialized { get; private set; } = "";

	/// <summary>Parsed enumeration of roommate SteamIds.</summary>
	public IEnumerable<long> RoommateIds
	{
		get
		{
			if ( string.IsNullOrWhiteSpace( RoommateIdsSerialized ) ) yield break;
			var parts = RoommateIdsSerialized.Split( ';', StringSplitOptions.RemoveEmptyEntries );
			foreach ( var part in parts )
			{
				if ( long.TryParse( part, out var id ) && id != 0L )
					yield return id;
			}
		}
	}

	/// <summary>True if the door type supports roommates (not blocked and not job-only).</summary>
	public bool CanHaveRoommates => !IsBlocked && !HasOnlyJobs;

	public bool IsRoommate( long steamId )
	{
		if ( steamId == 0L ) return false;
		foreach ( var id in RoommateIds )
			if ( id == steamId ) return true;
		return false;
	}

	/// <summary>True on the local client if the local player is listed as a roommate of this door.</summary>
	public bool IsLocalPlayerRoommate
	{
		get
		{
			var conn = Connection.Local;
			if ( conn is null ) return false;
			return IsRoommate( conn.SteamId.Value );
		}
	}

	private bool IsRoommate( ulong steamId ) => IsRoommate( unchecked( (long)steamId ) );

	/// <summary>Returns true if the given player currently holds one of the <see cref="AllowedJobs"/> (only meaningful when <see cref="HasOnlyJobs"/> is true).</summary>
	public bool IsJobAllowed( Player player )
	{
		if ( !HasOnlyJobs ) return false;
		if ( !player.IsValid() ) return false;
		if ( AllowedJobs is null || AllowedJobs.Count == 0 ) return false;

		var jobId = player.Job?.JobId;
		if ( string.IsNullOrWhiteSpace( jobId ) ) return false;

		foreach ( var allowedJob in AllowedJobs )
		{
			if ( string.Equals( allowedJob?.Id, jobId, StringComparison.OrdinalIgnoreCase ) )
				return true;
		}

		return false;
	}

	/// <summary>True on the local client if the local player holds one of the allowed jobs for this door.</summary>
	public bool IsLocalPlayerJobAllowed => IsJobAllowed( Player.Local );

	/// <summary>Current open/closed state of the door.</summary>
	[Sync( SyncFlags.FromHost )] public DoorState State { get; private set; } = DoorState.Closed;

	/// <summary>Current lock state of the door.</summary>
	[Sync( SyncFlags.FromHost )] public DoorLockState LockState { get; private set; } = DoorLockState.Unlocked;

	/// <summary>Yaw angle used by the current open animation. Chosen by the host from the opener side.</summary>
	[Sync( SyncFlags.FromHost )] public float OpenYaw { get; private set; } = 90f;

	/// <summary>True while this peer is playing the local open or close animation.</summary>
	public bool IsPlayingAnimation { get; private set; }

	/// <summary>True if the door has been broken.</summary>
	[Sync( SyncFlags.FromHost )] public bool IsBroken { get; private set; }

	/// <summary>How this door moves when it opens.</summary>
	[Property, Category( "Movement" )] public DoorMovementMode MovementMode { get; set; } = DoorMovementMode.Swing;

	/// <summary>Speed of the open/close rotation animation in degrees per second.</summary>
	[Property, Category( "Movement" ), ShowIf( "MovementMode", DoorMovementMode.Swing )] public float AnimationSpeed { get; set; } = 90f;

	/// <summary>Maximum angle, in degrees, used when the door opens.</summary>
	[Property, Category( "Movement" ), ShowIf( "MovementMode", DoorMovementMode.Swing )] public float OpenAngle { get; set; } = 90f;

	/// <summary>Local axis used by sliding doors. Negative <see cref="SlideDistance"/> moves in the opposite direction.</summary>
	[Property, Category( "Movement" ), ShowIf( "MovementMode", DoorMovementMode.Slide )] public DoorSlideAxis SlideAxis { get; set; } = DoorSlideAxis.LocalRight;

	/// <summary>Distance, in scene units, used by sliding doors. Use a negative value to slide left/back/down.</summary>
	[Property, Category( "Movement" ), ShowIf( "MovementMode", DoorMovementMode.Slide )] public float SlideDistance { get; set; } = 96f;

	/// <summary>Speed of the open/close sliding animation in scene units per second.</summary>
	[Property, Category( "Movement" ), ShowIf( "MovementMode", DoorMovementMode.Slide )] public float SlideSpeed { get; set; } = 120f;

	/// <summary>Maximum distance between a caller and the door for the host to accept direct door actions.</summary>
	[Property] public float InteractRange { get; set; } = 220f;

	/// <summary>Length of the view ray when resolving which door the player is looking at (HUD menu, keys).</summary>
	public const float MenuLookRayLength = 200f;

	/// <summary>Max distance from the player's eye to the door's world position to open the door HUD with Interact (F).</summary>
	public const float InteractOpenMenuMaxEyeDistance = 130f;

	/// <summary>
	/// Finds a door along the player's view ray. Optionally requires the eye-to-door distance to be within <paramref name="maxEyeToDoorDistance"/>.
	/// </summary>
	public static Door FindLookedAtDoor( Player player, float rayLength, float? maxEyeToDoorDistance = null )
	{
		if ( !player.IsValid() || !player.Controller.IsValid() ) return null;

		var eyePos = player.Controller.EyePosition;
		var eyeDir = player.Controller.EyeTransform.Forward;
		var len = MathF.Max( 1f, rayLength );

		var tr = player.Scene.Trace
			.Ray( eyePos, eyePos + eyeDir * len )
			.IgnoreGameObjectHierarchy( player.GameObject )
			.Run();

		if ( !tr.Hit ) return null;

		Door targetDoor = null;
		var go = tr.GameObject;
		while ( go.IsValid() )
		{
			if ( go.Components.TryGet<Door>( out var door, FindMode.EverythingInSelfAndParent ) )
			{
				targetDoor = door;
				break;
			}
			go = go.Parent;
		}

		if ( targetDoor is null ) return null;

		if ( maxEyeToDoorDistance.HasValue )
		{
			var maxD = MathF.Max( 1f, maxEyeToDoorDistance.Value );
			if ( Vector3.DistanceBetween( eyePos, targetDoor.WorldPosition ) > maxD )
				return null;
		}

		return targetDoor;
	}

	/// <summary>Sound played (broadcast to all clients at the door position) when this door opens.</summary>
	[Property, Category( "Sounds" )] public SoundEvent OpenSound { get; set; }

	/// <summary>Sound played (broadcast to all clients at the door position) when this door closes.</summary>
	[Property, Category( "Sounds" )] public SoundEvent CloseSound { get; set; }

	/// <summary>Sound played only for the buyer when this door is bought.</summary>
	[Property, Category( "Sounds" )] public SoundEvent BuySound { get; set; }

	/// <summary>Sound played only for the seller when this door is sold.</summary>
	[Property, Category( "Sounds" )] public SoundEvent SellSound { get; set; }

    /// <summary>
    /// True on the local client if the local player may lock/unlock this door with the Keys weapon:
    /// job-allowed for job doors, or owner/roommate for player-owned doors.
    /// </summary>
    public bool CanBeControlledByLocalPlayer
	{
		get
		{
			if ( IsBlocked ) return false;
			if ( HasOnlyJobs ) return IsLocalPlayerJobAllowed;
			if ( !HasOwner ) return false;
			return IsLocalPlayerOwner || IsLocalPlayerRoommate;
		}
	}

	// ===== Lockpick =====

	/// <summary>Maximum distance between the picker and the door for the host to accept an attempt.</summary>
	[Property, Category( "Lockpick" )] public float LockpickInteractRange { get; set; } = 120f;

	public string DoorHackName => Header;
	public Vector3 DoorHackWorldPosition => WorldPosition;
	public float DoorHackInteractRange => LockpickInteractRange;

	private float _currentYaw;
	private float _currentSlideDistance;
	private DoorState _animatedState = DoorState.Closed;
	private float _animatedOpenYaw = 90f;
	private Rotation _baseRotation;
	private Vector3 _baseLocalPosition;
	private TimeUntil _brokenLockout;
	private bool _hasPendingPrediction;
	private double _predictionCorrectionAt;

	private static string JobDeniedInteractionMessage => GameLocalization.Phrase( "notify.door.job_denied", "You cannot interact with this door." );
	private const double PredictionCorrectionDelaySeconds = 0.35;

	/// <summary>True while the post-break cooldown prevents closing or locking.</summary>
	private bool IsBrokenLocked => IsBroken && !_brokenLockout;

	protected override void OnStart()
	{
		ApplyStartLockState();

		_baseRotation = LocalRotation;
		_baseLocalPosition = LocalPosition;
		_currentYaw = State == DoorState.Open ? OpenYaw : 0f;
		_currentSlideDistance = State == DoorState.Open ? SlideDistance : 0f;
		_animatedState = State;
		_animatedOpenYaw = OpenYaw;
		ApplyCurrentTransform();

		if ( WorldHud.IsValid() )
			WorldHud.Door = this;
	}

	private void ApplyStartLockState()
	{
#if SERVER
		if ( !Networking.IsHost ) return;
		if ( !StartLocked ) return;
		if ( !HasOnlyJobs || IsBlocked ) return;

		LockState = DoorLockState.Locked;

		if ( DoorSecond.IsValid() && DoorSecond.HasOnlyJobs && !DoorSecond.IsBlocked )
			DoorSecond.LockState = DoorLockState.Locked;
#endif
	}

	protected override void OnUpdate()
	{
		if ( Networking.IsHost ) return;
		UpdateDoorAnimation();
	}

	protected override void OnFixedUpdate()
	{
		if ( !Networking.IsHost ) return;
		UpdateDoorAnimation();
	}

	private void UpdateDoorAnimation()
	{
		ReconcileAnimationWithSyncedState();

		if ( MovementMode == DoorMovementMode.Slide )
		{
			UpdateSlideAnimation();
			return;
		}

		UpdateSwingAnimation();
	}

	private void UpdateSwingAnimation()
	{
		float targetYaw = _animatedState == DoorState.Open ? _animatedOpenYaw : 0f;

		if ( !IsPlayingAnimation )
		{
			if ( MathF.Abs( _currentYaw - targetYaw ) > 0.001f )
			{
				_currentYaw = targetYaw;
				LocalRotation = _baseRotation * Rotation.FromYaw( _currentYaw );
			}
			return;
		}

		float step = MathF.Max( 0f, AnimationSpeed ) * Time.Delta;
		float diff = targetYaw - _currentYaw;

		if ( step <= 0.001f || MathF.Abs( diff ) <= step )
		{
			_currentYaw = targetYaw;
			IsPlayingAnimation = false;
		}
		else
		{
			_currentYaw += MathF.Sign( diff ) * step;
		}

		LocalRotation = _baseRotation * Rotation.FromYaw( _currentYaw );
	}

	private void ApplyCurrentTransform()
	{
		if ( MovementMode == DoorMovementMode.Slide )
			ApplySlidePosition();
		else
			LocalRotation = _baseRotation * Rotation.FromYaw( _currentYaw );
	}

	private void UpdateSlideAnimation()
	{
		float targetDistance = _animatedState == DoorState.Open ? SlideDistance : 0f;

		if ( !IsPlayingAnimation )
		{
			if ( MathF.Abs( _currentSlideDistance - targetDistance ) > 0.001f )
			{
				_currentSlideDistance = targetDistance;
				ApplySlidePosition();
			}
			return;
		}

		float step = MathF.Max( 0f, SlideSpeed ) * Time.Delta;
		float diff = targetDistance - _currentSlideDistance;

		if ( step <= 0.001f || MathF.Abs( diff ) <= step )
		{
			_currentSlideDistance = targetDistance;
			IsPlayingAnimation = false;
		}
		else
		{
			_currentSlideDistance += MathF.Sign( diff ) * step;
		}

		ApplySlidePosition();
	}

	private void ApplySlidePosition()
	{
		LocalPosition = _baseLocalPosition + GetSlideDirection() * _currentSlideDistance;
	}

	private Vector3 GetSlideDirection()
	{
		return SlideAxis switch
		{
			DoorSlideAxis.LocalForward => _baseRotation.Forward,
			DoorSlideAxis.LocalUp => _baseRotation.Up,
			_ => _baseRotation.Right
		};
	}

	private void ReconcileAnimationWithSyncedState()
	{
		if ( _hasPendingPrediction )
		{
			if ( Time.Now < _predictionCorrectionAt )
				return;

			_hasPendingPrediction = false;
		}

		if ( AnimationTargetMatches( State, OpenYaw ) )
			return;

		StartLocalMotion( State, OpenYaw );
	}

	private bool AnimationTargetMatches( DoorState targetState, float openYaw )
	{
		if ( _animatedState != targetState )
			return false;

		if ( targetState == DoorState.Open && MathF.Abs( _animatedOpenYaw - openYaw ) > 0.01f )
			return false;

		return true;
	}

	private void StartAuthoritativeMotion( DoorState targetState, float openYaw )
	{
		_hasPendingPrediction = false;
		StartLocalMotion( targetState, openYaw );
		RpcStartDoorMotion( (int)targetState, openYaw );
	}

	private void StartPredictedToggle( Player player )
	{
		if ( IsPlayingAnimation ) return;

		if ( State == DoorState.Closed )
		{
			if ( LockState == DoorLockState.Locked ) return;

			var openYaw = ResolveOpenYaw( player );
			StartPredictedMotion( DoorState.Open, openYaw );

			if ( DoorSecond.IsValid() )
				DoorSecond.StartPredictedMotion( DoorState.Open, -openYaw );

			return;
		}

		if ( IsBroken ) return;
		if ( LockState == DoorLockState.Locked ) return;

		StartPredictedMotion( DoorState.Closed, OpenYaw );

		if ( DoorSecond.IsValid() )
			DoorSecond.StartPredictedMotion( DoorState.Closed, DoorSecond.OpenYaw );
	}

	private void StartPredictedMotion( DoorState targetState, float openYaw )
	{
		_hasPendingPrediction = true;
		_predictionCorrectionAt = Time.Now + PredictionCorrectionDelaySeconds;
		StartLocalMotion( targetState, openYaw );
	}

	private void StartLocalMotion( DoorState targetState, float openYaw )
	{
		_animatedState = targetState;
		_animatedOpenYaw = openYaw;

		if ( MovementMode == DoorMovementMode.Slide )
		{
			var targetDistance = targetState == DoorState.Open ? SlideDistance : 0f;
			if ( MathF.Abs( _currentSlideDistance - targetDistance ) <= 0.001f )
			{
				_currentSlideDistance = targetDistance;
				IsPlayingAnimation = false;
				ApplySlidePosition();
				return;
			}
		}
		else
		{
			var targetYaw = targetState == DoorState.Open ? openYaw : 0f;
			if ( MathF.Abs( _currentYaw - targetYaw ) <= 0.001f )
			{
				_currentYaw = targetYaw;
				IsPlayingAnimation = false;
				LocalRotation = _baseRotation * Rotation.FromYaw( _currentYaw );
				return;
			}
		}

		IsPlayingAnimation = true;
	}

	[Rpc.Broadcast( NetFlags.UnreliableNoDelay )]
	private void RpcStartDoorMotion( int targetStateValue, float openYaw )
	{
		if ( !Networking.IsHost && Rpc.Caller is not null && !Rpc.Caller.IsHost ) return;

		var targetState = targetStateValue == (int)DoorState.Open ? DoorState.Open : DoorState.Closed;
		_hasPendingPrediction = false;
		StartLocalMotion( targetState, openYaw );
	}

	public bool Press( IPressable.Event e )
	{
		if ( !e.Source.GameObject.Components.TryGet<Player>( out var player, FindMode.EverythingInSelfAndParent ) ) return false;

		if ( Networking.IsHost )
		{
#if SERVER
			if ( ShouldDenyLockedJobDoorInteraction( player ) )
			{
				NotifyJobDeniedInteraction( GetPlayerConnection( player ) );
				return true;
			}

			Toggle( player );
#endif
		}
		else
		{
			StartPredictedToggle( player );
			RpcRequestToggle();
		}

		return true;
	}

	private void Toggle( Player player )
	{
#if SERVER
		if ( !Networking.IsHost ) return;

		if ( State == DoorState.Closed )
			Open( player );
		else
			Close();
#endif
	}

	[Rpc.Host]
	public void RpcRequestToggle()
	{
#if SERVER
		if ( !Networking.IsHost ) return;

		var caller = FindRpcCallerPlayer();
		if ( !CanPlayerInteract( caller ) ) return;
		if ( ShouldDenyLockedJobDoorInteraction( caller ) )
		{
			NotifyJobDeniedInteraction( Rpc.Caller );
			return;
		}

		Toggle( caller );
#endif
	}

	/// <summary>Opens the door. Has no effect if animating, already open, or locked. Mirrors to <see cref="DoorSecond"/>.</summary>
	public void Open( Player opener = null )
	{
#if SERVER
		if ( !Networking.IsHost ) return;
		OpenWithYaw( ResolveOpenYaw( opener ), true );
#endif
	}

	private void OpenPaired( float openYaw )
	{
#if SERVER
		if ( !Networking.IsHost ) return;
		OpenWithYaw( openYaw, false );
#endif
	}

	private void OpenWithYaw( float openYaw, bool mirrorToSecond )
	{
#if SERVER
		if ( State == DoorState.Open ) return; // idempotent — prevents paired-door recursion
		if ( IsPlayingAnimation ) return;
		if ( LockState == DoorLockState.Locked ) return;

		OpenYaw = openYaw;
		State = DoorState.Open;
		StartAuthoritativeMotion( DoorState.Open, openYaw );

		if ( OpenSound.IsValid() )
			RpcPlaySoundAtDoor( OpenSound );

		if ( mirrorToSecond && DoorSecond.IsValid() )
			DoorSecond.OpenPaired( -openYaw );
#endif
	}

	[Rpc.Host]
	public void RpcRequestOpen()
	{
#if SERVER
		if ( !Networking.IsHost ) return;

		var opener = FindRpcCallerPlayer();
		if ( !CanPlayerInteract( opener ) ) return;
		if ( ShouldDenyLockedJobDoorInteraction( opener ) )
		{
			NotifyJobDeniedInteraction( Rpc.Caller );
			return;
		}

		Open( opener );
#endif
	}

	/// <summary>Closes the door. Has no effect if animating, already closed, locked, or the broken lockout is active. Mirrors to <see cref="DoorSecond"/>.</summary>
	public void Close()
	{
#if SERVER
		if ( !Networking.IsHost ) return;
		if ( State == DoorState.Closed ) return; // idempotent — prevents paired-door recursion
		if ( IsPlayingAnimation ) return;
		if ( LockState == DoorLockState.Locked ) return;
		if ( IsBrokenLocked ) return;

		State = DoorState.Closed;
		StartAuthoritativeMotion( DoorState.Closed, OpenYaw );

		if ( CloseSound.IsValid() )
			RpcPlaySoundAtDoor( CloseSound );

		DoorSecond?.Close();
#endif
	}

	[Rpc.Host]
	public void RpcRequestClose()
	{
#if SERVER
		if ( !Networking.IsHost ) return;

		var closer = FindRpcCallerPlayer();
		if ( !CanPlayerInteract( closer ) ) return;
		if ( ShouldDenyLockedJobDoorInteraction( closer ) )
		{
			NotifyJobDeniedInteraction( Rpc.Caller );
			return;
		}

		Close();
#endif
	}

	/// <summary>Locks the door. Requires a non-blocked, non-broken, idle door that is either player-owned or a job-door. Mirrors to <see cref="DoorSecond"/>.</summary>
	public void Lock()
	{
#if SERVER
		if ( !Networking.IsHost ) return;
		if ( LockState == DoorLockState.Locked ) return; // idempotent — prevents paired-door recursion
		if ( IsPlayingAnimation ) return;
		if ( IsBrokenLocked ) return;
		if ( IsBlocked ) return;
		// Must be either a job-door or player-owned.
		if ( !HasOnlyJobs && !HasOwner ) return;

		LockState = DoorLockState.Locked;
		WorldHud?.WorldHudRefresh();

		DoorSecond?.Lock();
#endif
	}

	[Rpc.Host]
	public void RpcRequestLock()
	{
#if SERVER
		HostRequestLock( null );
#endif
	}

	[Rpc.Host]
	public void RpcRequestLockWithSound( SoundEvent successSound )
	{
#if SERVER
		HostRequestLock( successSound );
#endif
	}

	private void HostRequestLock( SoundEvent successSound )
	{
#if SERVER
		if ( !Networking.IsHost ) return;

		var caller = Rpc.Caller;
		var player = FindRpcCallerPlayer();
		if ( caller is null || !CanPlayerInteract( player ) ) return;
		if ( !CallerCanLock( caller.SteamId ) )
		{
			if ( HasOnlyJobs )
				NotifyJobDeniedInteraction( caller );
			return;
		}

		var wasUnlocked = LockState == DoorLockState.Unlocked;
		Lock();

		if ( wasUnlocked && LockState == DoorLockState.Locked && successSound.IsValid() )
			RpcPlaySoundAtDoor( successSound );
#endif
	}

	/// <summary>Unlocks the door. Requires the door not being admin-blocked and being either player-owned or a job-door. Mirrors to <see cref="DoorSecond"/>.</summary>
	public void Unlock()
	{
#if SERVER
		if ( !Networking.IsHost ) return;
		if ( LockState == DoorLockState.Unlocked ) return; // idempotent — prevents paired-door recursion
		if ( IsBlocked ) return;
		if ( !HasOnlyJobs && !HasOwner ) return;

		LockState = DoorLockState.Unlocked;
		WorldHud?.WorldHudRefresh();

		DoorSecond?.Unlock();
#endif
	}

	[Rpc.Host]
	public void RpcRequestUnlock()
	{
#if SERVER
		HostRequestUnlock( null );
#endif
	}

	[Rpc.Host]
	public void RpcRequestUnlockWithSound( SoundEvent successSound )
	{
#if SERVER
		HostRequestUnlock( successSound );
#endif
	}

	private void HostRequestUnlock( SoundEvent successSound )
	{
#if SERVER
		if ( !Networking.IsHost ) return;

		var caller = Rpc.Caller;
		var player = FindRpcCallerPlayer();
		if ( caller is null || !CanPlayerInteract( player ) ) return;
		if ( !CallerCanLock( caller.SteamId ) )
		{
			if ( HasOnlyJobs )
				NotifyJobDeniedInteraction( caller );
			return;
		}

		var wasLocked = LockState == DoorLockState.Locked;
		Unlock();

		if ( wasLocked && LockState == DoorLockState.Unlocked && successSound.IsValid() )
			RpcPlaySoundAtDoor( successSound );
#endif
	}

	/// <summary>Host-side check: is this caller allowed to toggle the door's lock state?</summary>
	private bool CallerCanLock( ulong steamId )
	{
		if ( HasOnlyJobs )
		{
			var caller = FindPlayerBySteamId( steamId );
			return IsJobAllowed( caller );
		}

		if ( IsOwnedBy( steamId ) ) return true;
		if ( IsRoommate( steamId ) ) return true;
		return false;
	}

	private bool ShouldDenyLockedJobDoorInteraction( Player player )
	{
		return HasOnlyJobs && LockState == DoorLockState.Locked && !IsJobAllowed( player );
	}

	/// <summary>Buys the door for the given player. Host-authoritative: validates blocked state, ownership, and funds.</summary>
	public bool Buy( Player buyer )
	{
#if SERVER
		if ( !Networking.IsHost ) return false;
		if ( !buyer.IsValid() ) return false;
		if ( IsBlocked ) return false;
		if ( HasOnlyJobs ) return false; // Job-only doors cannot be bought.
		if ( HasOwner ) return false;
		var buyPrice = Math.Max( 0, BuyPrice );
		if ( buyer.Money < buyPrice ) return false;
		if ( buyer.OwnedDoorsCount >= buyer.MaxDoors ) return false; // Player has reached their door limit.

		buyer.Money -= buyPrice;
		PlayerOwner = buyer;
		WorldHud?.WorldHudRefresh();

		// Mirror ownership to the paired door without charging again.
		if ( DoorSecond.IsValid() && !DoorSecond.HasOwner )
		{
			DoorSecond.PlayerOwner = buyer;
		}

		return true;
#else
		return false;
#endif
	}

	[Rpc.Host]
	public void RpcRequestBuy()
	{
#if SERVER
		if ( !Networking.IsHost ) return;

		var caller = Rpc.Caller;
		var buyer = FindRpcCallerPlayer();
		if ( buyer is null )
		{
			Log.Warning( $"Door.RpcRequestBuy: player not found for {caller?.DisplayName ?? "unknown"}" );
			return;
		}
		if ( !CanPlayerInteract( buyer ) ) return;

		if ( !Buy( buyer ) ) return;

		buyer.HostGrantAchievement( "buy_door" );

		NotifyDoorFeedback(
			caller,
			GameLocalization.Format( "notify.door.bought", "Door bought for ${0}. Doors: {1}/{2}.", BuyPrice, buyer.OwnedDoorsCount, buyer.MaxDoors ),
			NotificationType.Info,
			2.5f,
			BuySound );
#endif
	}

	/// <summary>Sells the door. Refunds half the buy price to the owner, clears ownership, and unlocks.</summary>
	public bool Sell()
	{
#if SERVER
		if ( !Networking.IsHost ) return false;
		if ( IsBlocked ) return false;
		if ( HasOnlyJobs ) return false; // Job-only doors cannot be sold.
		if ( !HasOwner ) return false;

		if ( PlayerOwner.IsValid() )
			PlayerOwner.Money += SellPrice; // Refund half, once — not doubled for paired doors.

		var partner = DoorSecond;

		PlayerOwner = null;
		LockState = DoorLockState.Unlocked;
		RoommateIdsSerialized = "";
		WorldHud?.WorldHudRefresh();

		// Clear ownership on the paired door without refunding again.
		if ( partner.IsValid() && partner.HasOwner )
		{
			partner.PlayerOwner = null;
			partner.LockState = DoorLockState.Unlocked;
			partner.RoommateIdsSerialized = "";
		}

		return true;
#else
		return false;
#endif
	}

	[Rpc.Host]
	public void RpcRequestSell()
	{
#if SERVER
		if ( !Networking.IsHost ) return;

		var caller = Rpc.Caller;
		var seller = FindRpcCallerPlayer();
		if ( caller is null || !CanPlayerInteract( seller ) ) return;

		// Only the gameplay owner can sell.
		if ( !IsOwnedBy( caller.SteamId ) ) return;
		if ( !Sell() ) return;

		NotifyDoorFeedback(
			caller,
			GameLocalization.Format( "notify.door.sold", "Door sold for ${0}. Doors: {1}/{2}.", SellPrice, seller.OwnedDoorsCount, seller.MaxDoors ),
			NotificationType.Info,
			2.5f,
			SellSound );
#endif
	}

	/// <summary>Returns true if the given SteamId matches the gameplay owner of this door.</summary>
	public bool IsOwnedBy( ulong steamId )
	{
		if ( !HasOwner ) return false;
		var ownerConn = PlayerOwner.Network.Owner;
		return ownerConn != null && ownerConn.SteamId == steamId;
	}

	/// <summary>
	/// Network event: when any player disconnects, free this door if they were the gameplay owner.
	/// Runs on every connected client; mutation is host-only.
	/// </summary>
	void Component.INetworkListener.OnDisconnected( Connection channel )
	{
#if SERVER
		if ( !Networking.IsHost ) return;
		if ( !HasOwner ) return;

		var ownerConn = PlayerOwner.Network.Owner;
		if ( ownerConn is null || ownerConn.SteamId != channel.SteamId ) return;

		// Owner left the game — release the door and clear roommates.
		PlayerOwner = null;
		LockState = DoorLockState.Unlocked;
		RoommateIdsSerialized = "";

		if ( DoorSecond.IsValid() )
		{
			DoorSecond.PlayerOwner = null;
			DoorSecond.LockState = DoorLockState.Unlocked;
			DoorSecond.RoommateIdsSerialized = "";
		}
#endif
	}

	/// <summary>Host-authoritative: add a roommate by SteamId. Only the current owner may call.</summary>
	[Rpc.Host]
	public void RpcRequestAddRoommate( long steamId )
	{
#if SERVER
		if ( !Networking.IsHost ) return;

		var caller = Rpc.Caller;
		var owner = FindRpcCallerPlayer();
		if ( caller is null || !CanPlayerInteract( owner ) ) return;

		if ( !CanHaveRoommates ) return;
		if ( !HasOwner ) return;
		if ( !IsOwnedBy( caller.SteamId ) ) return; // only the owner can add
		if ( steamId == 0L ) return;
		if ( steamId == OwnerSteamId ) return; // cannot add self

		var set = new HashSet<long>( RoommateIds );
		if ( !set.Add( steamId ) ) return; // already a roommate

		var serialized = string.Join( ";", set );
		RoommateIdsSerialized = serialized;

		if ( DoorSecond.IsValid() )
			DoorSecond.RoommateIdsSerialized = serialized;
#endif
	}

	/// <summary>Host-authoritative: remove a roommate by SteamId. Only the current owner may call.</summary>
	[Rpc.Host]
	public void RpcRequestRemoveRoommate( long steamId )
	{
#if SERVER
		if ( !Networking.IsHost ) return;

		var caller = Rpc.Caller;
		var owner = FindRpcCallerPlayer();
		if ( caller is null || !CanPlayerInteract( owner ) ) return;

		if ( !CanHaveRoommates ) return;
		if ( !HasOwner ) return;
		if ( !IsOwnedBy( caller.SteamId ) ) return; // only the owner can remove
		if ( steamId == 0L ) return;

		var set = new HashSet<long>( RoommateIds );
		if ( !set.Remove( steamId ) ) return;

		var serialized = string.Join( ";", set );
		RoommateIdsSerialized = serialized;

		if ( DoorSecond.IsValid() )
			DoorSecond.RoommateIdsSerialized = serialized;
#endif
	}

	/// <summary>Broadcast-play a sound at this door's world position on all clients.</summary>
	[Rpc.Broadcast]
	private void RpcPlaySoundAtDoor( SoundEvent sound )
	{
		if ( !Networking.IsHost && Rpc.Caller is not null && !Rpc.Caller.IsHost ) return;
		if ( !sound.IsValid() ) return;
		Sound.Play( sound, WorldPosition );
	}

	/// <summary>
	/// Client → Host → Broadcast: play the "hit" sound at the door for everyone.
	/// Used when a player who is neither the owner nor a roommate tries to interact
	/// with the Keys weapon — they only hit the door without changing state.
	/// </summary>
	[Rpc.Host]
	public void RpcRequestHit( SoundEvent hitSound )
	{
#if SERVER
		if ( !Networking.IsHost ) return;
		var player = FindRpcCallerPlayer();
		if ( !CanPlayerInteract( player ) ) return;
		if ( HasOnlyJobs && !IsJobAllowed( player ) )
			NotifyJobDeniedInteraction( Rpc.Caller );
		if ( !hitSound.IsValid() ) return;
		RpcPlaySoundAtDoor( hitSound );
#endif
	}

	private Player FindPlayerBySteamId( ulong steamId )
	{
		foreach ( var go in Scene.GetAllObjects( true ) )
		{
			if ( !go.Components.TryGet<Player>( out var ply ) ) continue;
			if ( ply.GameObject.Network.Owner?.SteamId == steamId ) return ply;
		}
		return null;
	}

	private Player FindRpcCallerPlayer()
	{
		var caller = Rpc.Caller;
		if ( caller is null ) return null;
		return FindPlayerBySteamId( caller.SteamId );
	}

	private static Connection GetPlayerConnection( Player player )
	{
		return player.IsValid() ? player.Network.Owner : null;
	}

	private void NotifyDoorFeedback( Connection target, string text, NotificationType type, float aliveSeconds, SoundEvent sound )
	{
#if SERVER
		if ( target is null ) return;

		using ( Rpc.FilterInclude( c => c.SteamId.Value == target.SteamId.Value ) )
		{
			RpcShowDoorFeedback( text, (int)type, aliveSeconds, sound );
		}
#endif
	}

	[Rpc.Broadcast]
	private void RpcShowDoorFeedback( string text, int type, float aliveSeconds, SoundEvent sound )
	{
		if ( !Networking.IsHost && Rpc.Caller is not null && !Rpc.Caller.IsHost ) return;

		var notificationType = (NotificationType)type;
		switch ( notificationType )
		{
			case NotificationType.Warn: Notification.Warn( text, aliveSeconds ); break;
			case NotificationType.Error: Notification.Error( text, aliveSeconds ); break;
			default: Notification.Info( text, aliveSeconds ); break;
		}

		if ( sound.IsValid() )
			Sound.Play( sound, WorldPosition );
	}

	private static void NotifyJobDeniedInteraction( Connection target )
	{
#if SERVER
		if ( target is null ) return;

		using ( Rpc.FilterInclude( c => c.SteamId.Value == target.SteamId.Value ) )
		{
			RpcShowDoorInteractionNotification( JobDeniedInteractionMessage, (int)NotificationType.Warn, 2.5f );
		}
#endif
	}

	[Rpc.Broadcast]
	private static void RpcShowDoorInteractionNotification( string text, int type, float aliveSeconds )
	{
		var notificationType = (NotificationType)type;
		switch ( notificationType )
		{
			case NotificationType.Warn: Notification.Warn( text, aliveSeconds ); break;
			case NotificationType.Error: Notification.Error( text, aliveSeconds ); break;
			default: Notification.Info( text, aliveSeconds ); break;
		}
	}

	private bool CanPlayerInteract( Player player )
	{
		if ( !player.IsValid() ) return false;

		var maxDistance = MathF.Max( 1f, InteractRange );
		return Vector3.DistanceBetween( GetPlayerInteractionPosition( player ), WorldPosition ) <= maxDistance;
	}

	private float ResolveOpenYaw( Player opener )
	{
		var openAngle = MathF.Abs( OpenAngle );
		if ( openAngle <= 0.001f ) return 0f;
		if ( !opener.IsValid() ) return openAngle;

		var doorNormal = FlattenHorizontal( WorldRotation.Forward );
		if ( doorNormal.LengthSquared <= 0.001f ) return openAngle;
		doorNormal = doorNormal.Normal;

		var openerOffset = FlattenHorizontal( GetPlayerInteractionPosition( opener ) - WorldPosition );
		var side = openerOffset.LengthSquared > 0.001f ? Vector3.Dot( doorNormal, openerOffset ) : 0f;

		if ( MathF.Abs( side ) <= 2f )
		{
			var openerForward = FlattenHorizontal( GetPlayerForward( opener ) );
			if ( openerForward.LengthSquared > 0.001f )
				side = -Vector3.Dot( doorNormal, openerForward.Normal );
		}

		if ( MathF.Abs( side ) <= 0.001f ) return openAngle;
		return side > 0f ? openAngle : -openAngle;
	}

	private static Vector3 GetPlayerInteractionPosition( Player player )
	{
		if ( player.Controller.IsValid() )
			return player.Controller.EyePosition;

		return player.WorldPosition;
	}

	private static Vector3 GetPlayerForward( Player player )
	{
		if ( player.Controller.IsValid() )
			return player.Controller.EyeTransform.Forward;

		return player.WorldRotation.Forward;
	}

	private static Vector3 FlattenHorizontal( Vector3 value )
	{
		return new Vector3( value.x, value.y, 0f );
	}

	/// <summary>
	/// Breaks the door: unlocks it, forces it open, and prevents closing or locking
	/// for a duration defined by the broken lockout timer.
	/// </summary>
	public void Break( Player breaker = null )
	{
#if SERVER
		if ( !Networking.IsHost ) return;
		if ( IsBroken ) return;

		IsBroken = true;
		LockState = DoorLockState.Unlocked;
		_brokenLockout = 10f;

		if ( State == DoorState.Closed && !IsPlayingAnimation )
			Open( breaker );
#endif
	}

	[Rpc.Host]
	public void RpcRequestBreak()
	{
#if SERVER
		if ( !Networking.IsHost ) return;

		var breaker = FindRpcCallerPlayer();
		if ( !CanPlayerInteract( breaker ) ) return;

		Break( breaker );
#endif
	}

	// ===================== LOCKPICK =====================

	/// <summary>True if this door is currently a valid candidate for a fresh lockpick attempt.</summary>
	public bool CanBeLockpicked()
	{
		return CanBeDoorHacked( Player.Local );
	}

	public bool CanBeDoorHacked( Player hacker )
	{
		if ( IsBlocked ) return false;
		if ( IsBroken ) return false;
		if ( LockState != DoorLockState.Locked ) return false;
		if ( hacker.IsValid() && hacker.IsArrested ) return false;
		// Только покупные (с владельцем) или job-двери — на остальных замок не имеет смысла.
		if ( !HasOnlyJobs && !HasOwner ) return false;
		return true;
	}

	/// <summary>Client request: try to start a lockpick attempt on this door. Host-authoritative.</summary>
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

	public void HostOnDoorHackSucceeded( Player hacker )
	{
#if SERVER
		if ( !Networking.IsHost ) return;
		if ( !CanBeDoorHacked( hacker ) ) return;

		Unlock();
		Open( hacker );
#endif
	}

	public void HostOnDoorHackFailed( Player hacker )
	{
#if SERVER
		if ( !Networking.IsHost ) return;
#endif
	}
}
