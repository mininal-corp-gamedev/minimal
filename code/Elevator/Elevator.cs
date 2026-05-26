using Sandbox;
using System;
using System.Collections.Generic;

public enum ElevatorTravelDirection
{
	Down = -1,
	Idle = 0,
	Up = 1
}

public sealed class Elevator : Component
{
	private const int MinFloor = 1;
	private const int MaxFloorCount = 20;
	private const float ArrivalEpsilon = 0.1f;
	private const float PassengerFeetBelowPlatformTolerance = 16f;
	private const float PassengerFeetAbovePlatformTolerance = 28f;
	private const float PassengerDeckRetentionAboveTolerance = 96f;

	[Property, Group( "Setup" )] public GameObject Platform { get; set; }
	[Property, Group( "Setup" )] public int FloorCount { get; set; } = 5;
	[Property, Group( "Setup" )] public int InitialFloor { get; set; } = 1;
	[Property, Group( "Setup" )] public float FloorHeight { get; set; } = 128f;
	[Property, Group( "Setup" )] public bool UseCustomFloorHeights { get; set; } = false;
	[Property, Group( "Setup" ), ShowIf( "UseCustomFloorHeights", true )]
	public List<float> FloorHeights { get; set; } = new() { 128f, 128f, 128f, 128f };

	[Property, Group( "Movement" )] public float MoveSpeed { get; set; } = 128f;
	[Property, Group( "Doors" )] public bool UseDoors { get; set; } = true;
	[Property, Group( "Doors" )] public float DoorOpenSeconds { get; set; } = 2.4f;
	[Property, Group( "Doors" )] public float DoorCloseSeconds { get; set; } = 0.8f;
	[Property, Group( "Gameplay" )] public float MaxUseDistance { get; set; } = 260f;
	[Property, Group( "Gameplay" )] public bool RejectRequestsWhileMoving { get; set; } = false;
	[Property, Group( "Passenger Carry" )] public bool CarryPlayers { get; set; } = true;
	[Property, Group( "Passenger Carry" )] public float PassengerTraceDistance { get; set; } = 64f;
	[Property, Group( "Passenger Carry" )] public float PassengerHorizontalRadius { get; set; } = 160f;
	[Property, Group( "Passenger Carry" )] public float PassengerReleaseGraceSeconds { get; set; } = 0.28f;

	[Property, Group( "Sounds" )] public SoundEvent StartSound { get; set; }
	[Property, Group( "Sounds" )] public SoundEvent ArriveSound { get; set; }

	[Sync( SyncFlags.FromHost )] public int CurrentFloor { get; private set; } = 1;
	[Sync( SyncFlags.FromHost )] public int TargetFloor { get; private set; } = 1;
	[Sync( SyncFlags.FromHost | SyncFlags.Interpolate )] public float TravelOffset { get; private set; }
	[Sync( SyncFlags.FromHost )] public bool IsMoving { get; private set; }
	[Sync( SyncFlags.FromHost )] public bool DoorsOpen { get; private set; }
	[Sync( SyncFlags.FromHost )] public int OpenDoorFloor { get; private set; }
	[Sync( SyncFlags.FromHost )] public ElevatorTravelDirection Direction { get; private set; } = ElevatorTravelDirection.Idle;
	[Sync( SyncFlags.FromHost )] public string PendingCabFloors { get; private set; } = "";
	[Sync( SyncFlags.FromHost )] public string PendingUpHallFloors { get; private set; } = "";
	[Sync( SyncFlags.FromHost )] public string PendingDownHallFloors { get; private set; } = "";
	[Sync( SyncFlags.FromHost )] public int QueueRevision { get; private set; }

	public int SafeFloorCount => Math.Clamp( FloorCount, MinFloor, MaxFloorCount );
	public float TargetOffset => GetOffsetForFloor( TargetFloor );
	public float CurrentOffset => TravelOffset;
	public bool IsIdle => !IsMoving;

	private Vector3 _baseLocalPosition;
	private bool _hasBasePosition;
	private readonly HashSet<long> _passengerSteamIds = new();
	private readonly Dictionary<long, float> _passengerLastStandingTimeBySteamId = new();
	private readonly SortedSet<int> _cabRequests = new();
	private readonly SortedSet<int> _upHallRequests = new();
	private readonly SortedSet<int> _downHallRequests = new();
	private bool _doorSequenceActive;
	private double _doorCloseAt;
	private double _doorReadyAt;
	private bool _playStartSoundOnNextMove;

	protected override void OnStart()
	{
		CacheBasePosition();

		if ( Networking.IsHost )
		{
			var initialFloor = ClampFloor( InitialFloor );
			CurrentFloor = initialFloor;
			TargetFloor = initialFloor;
			TravelOffset = GetOffsetForFloor( initialFloor );
			IsMoving = false;
			DoorsOpen = false;
			OpenDoorFloor = 0;
			Direction = ElevatorTravelDirection.Idle;
			SyncPendingRequests();
		}

		ApplyPlatformPosition();
	}

	protected override void OnUpdate()
	{
		// The platform has a collider, so its authoritative pose is applied from
		// FixedUpdate. Moving it here as well makes the player's physics tick fight
		// render-tick transform changes.
	}

	protected override void OnFixedUpdate()
	{
		CacheBasePosition();

		var platform = GetPlatform();
		var previousPlatformPosition = platform.IsValid() ? platform.WorldPosition : WorldPosition;

		if ( !Networking.IsHost )
		{
			var carryLocalPassenger = ShouldCarryLocalPassenger();
			ApplyPlatformPosition();
			var localDelta = GetPlatformMovementDelta( previousPlatformPosition );
			ApplyPlatformSurfaceVelocity( localDelta );
			CarryLocalPassenger( localDelta, carryLocalPassenger );
			return;
		}

		NormalizeHostFloors();
		RefreshPassengers();
		if ( UpdateDoorSequence() )
		{
			ApplyPlatformPosition();
			ApplyPlatformSurfaceVelocity( Vector3.Zero );
			return;
		}

		UpdateQueueTarget();

		var targetOffset = GetOffsetForFloor( TargetFloor );
		var diff = targetOffset - TravelOffset;
		var speed = MathF.Max( 1f, MoveSpeed );
		var step = speed * Time.Delta;

		if ( !IsMoving && MathF.Abs( diff ) <= ArrivalEpsilon )
		{
			ApplyPlatformPosition();
			ApplyPlatformSurfaceVelocity( Vector3.Zero );
			return;
		}

		if ( MathF.Abs( diff ) <= MathF.Max( ArrivalEpsilon, step ) )
		{
			var wasMoving = IsMoving;

			TravelOffset = targetOffset;
			CurrentFloor = TargetFloor;
			CompleteStopAtFloor( CurrentFloor );
			ApplyPlatformPosition();
			var arriveDelta = GetPlatformMovementDelta( previousPlatformPosition );
			ApplyPlatformSurfaceVelocity( arriveDelta );
			CarryPassengers( arriveDelta );

			if ( wasMoving && ArriveSound.IsValid() )
				RpcPlaySoundAtPlatform( ArriveSound );

			return;
		}

		IsMoving = true;
		Direction = diff > 0f ? ElevatorTravelDirection.Up : ElevatorTravelDirection.Down;
		TravelOffset += MathF.Sign( diff ) * step;
		ApplyPlatformPosition();
		var moveDelta = GetPlatformMovementDelta( previousPlatformPosition );
		ApplyPlatformSurfaceVelocity( moveDelta );
		CarryPassengers( moveDelta );
	}

	public bool CanRequestFloor( int floor )
	{
		if ( floor < MinFloor || floor > SafeFloorCount )
			return false;

		if ( RejectRequestsWhileMoving && IsMoving )
			return false;

		if ( HasPendingCabRequest( floor ) )
			return false;

		if ( !IsMoving && floor == CurrentFloor )
			return UseDoors && !IsDoorOpenAtFloor( floor );

		return IsMoving || floor != CurrentFloor;
	}

	public bool CanRequestHallCall( int floor, ElevatorTravelDirection direction )
	{
		if ( floor < MinFloor || floor > SafeFloorCount )
			return false;

		if ( RejectRequestsWhileMoving && IsMoving )
			return false;

		var normalizedDirection = NormalizeHallDirection( floor, direction );
		if ( normalizedDirection == ElevatorTravelDirection.Idle )
			return false;

		if ( HasPendingHallCall( floor, normalizedDirection ) )
			return false;

		if ( !IsMoving && floor == CurrentFloor )
			return UseDoors && !IsDoorOpenAtFloor( floor );

		return IsMoving || floor != CurrentFloor;
	}

	public bool HasPendingCabRequest( int floor )
	{
		return ContainsSerializedFloor( PendingCabFloors, ClampFloor( floor ) );
	}

	public bool HasPendingHallCall( int floor, ElevatorTravelDirection direction )
	{
		var normalizedDirection = NormalizeHallDirection( floor, direction );
		return normalizedDirection == ElevatorTravelDirection.Up
			? ContainsSerializedFloor( PendingUpHallFloors, ClampFloor( floor ) )
			: normalizedDirection == ElevatorTravelDirection.Down
				&& ContainsSerializedFloor( PendingDownHallFloors, ClampFloor( floor ) );
	}

	[Rpc.Host]
	public void RpcRequestFloor( int floor )
	{
		if ( !Networking.IsHost )
			return;

		HostTryRequestFloor( floor, Rpc.Caller );
	}

	[Rpc.Host]
	public void RpcRequestFloorFromPanel( int floor, Vector3 clientWorldPosition, Vector3 clientEyePosition )
	{
		if ( !Networking.IsHost )
			return;

		HostTryRequestFloor( floor, Rpc.Caller, clientWorldPosition, clientEyePosition );
	}

	private void HostTryRequestFloor( int floor, Connection caller, Vector3? clientWorldPosition = null, Vector3? clientEyePosition = null )
	{
		if ( caller is null )
			return;

		if ( !CanCallerUseElevator( caller, clientWorldPosition, clientEyePosition ) )
		{
			NotifyCaller( caller, GameLocalization.Phrase( "notify.elevator.too_far", "Too far from elevator." ), NotificationType.Warn );
			return;
		}

		HostQueueCabRequest( floor );
	}

	[Rpc.Host]
	public void RpcRequestHallCallFromPanel( int floor, int direction, Vector3 clientWorldPosition, Vector3 clientEyePosition )
	{
		if ( !Networking.IsHost )
			return;

		HostTryRequestHallCall( floor, (ElevatorTravelDirection)Math.Clamp( direction, -1, 1 ), Rpc.Caller, clientWorldPosition, clientEyePosition );
	}

	private void HostTryRequestHallCall( int floor, ElevatorTravelDirection direction, Connection caller, Vector3? clientWorldPosition = null, Vector3? clientEyePosition = null )
	{
		if ( caller is null )
			return;

		var normalizedFloor = ClampFloor( floor );
		if ( !CanCallerUseElevatorAtFloor( caller, normalizedFloor, clientWorldPosition, clientEyePosition ) )
		{
			NotifyCaller( caller, GameLocalization.Phrase( "notify.elevator.too_far", "Too far from elevator." ), NotificationType.Warn );
			return;
		}

		HostQueueHallRequest( normalizedFloor, direction );
	}

	public void HostSetTargetFloor( int floor )
	{
		if ( !Networking.IsHost )
			return;

		HostQueueCabRequest( floor );
	}

	private void HostQueueCabRequest( int floor )
	{
		if ( !Networking.IsHost || !CanRequestFloor( floor ) )
			return;

		var target = ClampFloor( floor );
		if ( IsAtFloor( target ) )
		{
			CurrentFloor = target;
			TargetFloor = target;
			TravelOffset = GetOffsetForFloor( target );
			Direction = ElevatorTravelDirection.Idle;
			ApplyPlatformPosition();
			OpenDoorsAtFloor( target );
			SyncPendingRequests();
			return;
		}

		_cabRequests.Add( target );
		SyncPendingRequests();
		UpdateQueueTarget( true );
	}

	private void HostQueueHallRequest( int floor, ElevatorTravelDirection direction )
	{
		if ( !Networking.IsHost )
			return;

		var target = ClampFloor( floor );
		var normalizedDirection = NormalizeHallDirection( target, direction );
		if ( !CanRequestHallCall( target, normalizedDirection ) )
			return;

		if ( IsAtFloor( target ) && !IsMoving )
		{
			CurrentFloor = target;
			TargetFloor = target;
			TravelOffset = GetOffsetForFloor( target );
			Direction = ElevatorTravelDirection.Idle;
			ApplyPlatformPosition();
			OpenDoorsAtFloor( target );
			SyncPendingRequests();
			return;
		}

		GetHallRequestSet( normalizedDirection ).Add( target );
		SyncPendingRequests();
		UpdateQueueTarget( true );
	}

	private void UpdateQueueTarget( bool playStartSound = false )
	{
		if ( !Networking.IsHost )
			return;

		if ( IsDoorSequenceActive() )
		{
			if ( playStartSound )
				_playStartSoundOnNextMove = true;

			IsMoving = false;
			TargetFloor = CurrentFloor;
			return;
		}

		ServiceCurrentFloorRequests();
		if ( IsDoorSequenceActive() )
			return;

		if ( !HasPendingRequests() )
		{
			IsMoving = false;
			Direction = ElevatorTravelDirection.Idle;
			TargetFloor = CurrentFloor;
			_playStartSoundOnNextMove = false;
			return;
		}

		var wasMoving = IsMoving;
		var nextFloor = SelectNextStop( out var nextDirection );
		if ( !nextFloor.HasValue )
		{
			IsMoving = false;
			Direction = ElevatorTravelDirection.Idle;
			TargetFloor = CurrentFloor;
			return;
		}

		TargetFloor = nextFloor.Value;
		Direction = nextDirection;

		var targetOffset = GetOffsetForFloor( TargetFloor );
		var diff = targetOffset - TravelOffset;
		if ( MathF.Abs( diff ) <= ArrivalEpsilon )
		{
			CompleteStopAtFloor( TargetFloor );
			return;
		}

		IsMoving = true;
		Direction = diff > 0f ? ElevatorTravelDirection.Up : ElevatorTravelDirection.Down;

		if ( (playStartSound || _playStartSoundOnNextMove) && !wasMoving && StartSound.IsValid() )
			RpcPlaySoundAtPlatform( StartSound );

		_playStartSoundOnNextMove = false;
	}

	private void CompleteStopAtFloor( int floor )
	{
		CurrentFloor = ClampFloor( floor );
		TravelOffset = GetOffsetForFloor( CurrentFloor );
		TargetFloor = CurrentFloor;

		ClearServedRequestsAtFloor( CurrentFloor );
		SyncPendingRequests();

		if ( UseDoors )
		{
			IsMoving = false;
			TargetFloor = CurrentFloor;
			if ( !HasPendingRequests() )
				Direction = ElevatorTravelDirection.Idle;
			else
				_playStartSoundOnNextMove = true;

			OpenDoorsAtFloor( CurrentFloor );
			return;
		}

		if ( !HasPendingRequests() )
		{
			IsMoving = false;
			Direction = ElevatorTravelDirection.Idle;
			return;
		}

		var nextFloor = SelectNextStop( out var nextDirection );
		if ( !nextFloor.HasValue )
		{
			IsMoving = false;
			Direction = ElevatorTravelDirection.Idle;
			return;
		}

		TargetFloor = nextFloor.Value;
		var diff = GetOffsetForFloor( TargetFloor ) - TravelOffset;
		if ( MathF.Abs( diff ) <= ArrivalEpsilon )
		{
			IsMoving = false;
			Direction = ElevatorTravelDirection.Idle;
			return;
		}

		IsMoving = true;
		Direction = diff > 0f ? ElevatorTravelDirection.Up : ElevatorTravelDirection.Down;
	}

	private bool UpdateDoorSequence()
	{
		if ( !IsDoorSequenceActive() )
			return false;

		IsMoving = false;
		TargetFloor = CurrentFloor;

		if ( Time.Now >= _doorCloseAt )
			DoorsOpen = false;

		if ( Time.Now < _doorReadyAt )
			return true;

		_doorSequenceActive = false;
		DoorsOpen = false;
		OpenDoorFloor = 0;
		return false;
	}

	private void OpenDoorsAtFloor( int floor )
	{
		if ( !UseDoors )
			return;

		var targetFloor = ClampFloor( floor );
		CurrentFloor = targetFloor;
		TargetFloor = targetFloor;
		IsMoving = false;
		DoorsOpen = true;
		OpenDoorFloor = targetFloor;
		_doorSequenceActive = true;

		var holdSeconds = MathF.Max( 0.05f, DoorOpenSeconds );
		var closeSeconds = MathF.Max( 0.05f, DoorCloseSeconds );
		_doorCloseAt = Time.Now + holdSeconds;
		_doorReadyAt = _doorCloseAt + closeSeconds;
	}

	private bool IsDoorSequenceActive()
	{
		return UseDoors && _doorSequenceActive;
	}

	public bool IsDoorOpenAtFloor( int floor )
	{
		return DoorsOpen && OpenDoorFloor == ClampFloor( floor );
	}

	private void ServiceCurrentFloorRequests()
	{
		var floor = GetNearestFloorForOffset( TravelOffset );
		if ( !IsAtFloor( floor ) )
			return;

		CurrentFloor = floor;
		if ( !HasAnyRequestAtFloor( floor ) )
			return;

		if ( Direction == ElevatorTravelDirection.Idle )
		{
			_cabRequests.Remove( floor );
			_upHallRequests.Remove( floor );
			_downHallRequests.Remove( floor );
		}
		else
		{
			ClearServedRequestsAtFloor( floor );
		}

		SyncPendingRequests();
		if ( UseDoors )
		{
			IsMoving = false;
			TargetFloor = CurrentFloor;
			if ( !HasPendingRequests() )
				Direction = ElevatorTravelDirection.Idle;
			else
				_playStartSoundOnNextMove = true;

			OpenDoorsAtFloor( floor );
		}
	}

	private void ClearServedRequestsAtFloor( int floor )
	{
		var servedFloor = ClampFloor( floor );
		_cabRequests.Remove( servedFloor );

		var direction = Direction;
		if ( direction == ElevatorTravelDirection.Up )
		{
			_upHallRequests.Remove( servedFloor );
			if ( !HasServiceableStopsAhead( ElevatorTravelDirection.Up ) )
				_downHallRequests.Remove( servedFloor );
		}
		else if ( direction == ElevatorTravelDirection.Down )
		{
			_downHallRequests.Remove( servedFloor );
			if ( !HasServiceableStopsAhead( ElevatorTravelDirection.Down ) )
				_upHallRequests.Remove( servedFloor );
		}
		else
		{
			_upHallRequests.Remove( servedFloor );
			_downHallRequests.Remove( servedFloor );
		}
	}

	private int? SelectNextStop( out ElevatorTravelDirection nextDirection )
	{
		nextDirection = ElevatorTravelDirection.Idle;
		if ( !HasPendingRequests() )
			return null;

		var preferredDirection = Direction == ElevatorTravelDirection.Idle
			? ChooseIdleDirection()
			: Direction;

		if ( preferredDirection != ElevatorTravelDirection.Idle )
		{
			var preferredStop = FindNextServiceableStop( preferredDirection );
			if ( preferredStop.HasValue )
			{
				nextDirection = preferredDirection;
				return preferredStop.Value;
			}

			var reversalStop = FindNextOppositeHallStopAhead( preferredDirection );
			if ( reversalStop.HasValue )
			{
				nextDirection = preferredDirection;
				return reversalStop.Value;
			}

			var oppositeDirection = OppositeDirection( preferredDirection );
			var oppositeStop = FindNextServiceableStop( oppositeDirection );
			if ( oppositeStop.HasValue )
			{
				nextDirection = oppositeDirection;
				return oppositeStop.Value;
			}

			var oppositeReversalStop = FindNextOppositeHallStopAhead( oppositeDirection );
			if ( oppositeReversalStop.HasValue )
			{
				nextDirection = oppositeDirection;
				return oppositeReversalStop.Value;
			}
		}

		return FindClosestPendingStop( out nextDirection );
	}

	private ElevatorTravelDirection ChooseIdleDirection()
	{
		var nextFloor = FindClosestPendingStop( out var direction );
		return nextFloor.HasValue ? direction : ElevatorTravelDirection.Idle;
	}

	private int? FindNextServiceableStop( ElevatorTravelDirection direction )
	{
		if ( direction == ElevatorTravelDirection.Idle )
			return null;

		int? bestFloor = null;
		foreach ( var floor in GetPendingFloors() )
		{
			if ( !IsFloorAhead( floor, direction ) )
				continue;

			if ( !_cabRequests.Contains( floor ) && !GetHallRequestSet( direction ).Contains( floor ) )
				continue;

			bestFloor = SelectBetterFloorInDirection( bestFloor, floor, direction );
		}

		return bestFloor;
	}

	private int? FindNextOppositeHallStopAhead( ElevatorTravelDirection direction )
	{
		if ( direction == ElevatorTravelDirection.Idle )
			return null;

		var opposite = OppositeDirection( direction );
		var requests = GetHallRequestSet( opposite );
		int? bestFloor = null;
		foreach ( var floor in requests )
		{
			if ( !IsFloorAhead( floor, direction ) )
				continue;

			bestFloor = SelectBetterFloorInDirection( bestFloor, floor, direction );
		}

		return bestFloor;
	}

	private int? FindClosestPendingStop( out ElevatorTravelDirection direction )
	{
		direction = ElevatorTravelDirection.Idle;
		int? bestFloor = null;
		var bestDistance = float.MaxValue;

		foreach ( var floor in GetPendingFloors() )
		{
			var distance = MathF.Abs( GetOffsetForFloor( floor ) - TravelOffset );
			if ( distance >= bestDistance )
				continue;

			bestFloor = floor;
			bestDistance = distance;
		}

		if ( !bestFloor.HasValue )
			return null;

		var diff = GetOffsetForFloor( bestFloor.Value ) - TravelOffset;
		if ( MathF.Abs( diff ) <= ArrivalEpsilon )
		{
			if ( _upHallRequests.Contains( bestFloor.Value ) && !_downHallRequests.Contains( bestFloor.Value ) )
				direction = ElevatorTravelDirection.Up;
			else if ( _downHallRequests.Contains( bestFloor.Value ) && !_upHallRequests.Contains( bestFloor.Value ) )
				direction = ElevatorTravelDirection.Down;
			else
				direction = ElevatorTravelDirection.Idle;
		}
		else
		{
			direction = diff > 0f ? ElevatorTravelDirection.Up : ElevatorTravelDirection.Down;
		}

		return bestFloor.Value;
	}

	private bool HasServiceableStopsAhead( ElevatorTravelDirection direction )
	{
		return FindNextServiceableStop( direction ).HasValue;
	}

	private bool HasPendingRequests()
	{
		return _cabRequests.Count > 0 || _upHallRequests.Count > 0 || _downHallRequests.Count > 0;
	}

	private bool HasAnyRequestAtFloor( int floor )
	{
		var target = ClampFloor( floor );
		return _cabRequests.Contains( target ) || _upHallRequests.Contains( target ) || _downHallRequests.Contains( target );
	}

	private IEnumerable<int> GetPendingFloors()
	{
		var seen = new HashSet<int>();
		foreach ( var floor in _cabRequests )
		{
			if ( seen.Add( floor ) )
				yield return floor;
		}

		foreach ( var floor in _upHallRequests )
		{
			if ( seen.Add( floor ) )
				yield return floor;
		}

		foreach ( var floor in _downHallRequests )
		{
			if ( seen.Add( floor ) )
				yield return floor;
		}
	}

	private int? SelectBetterFloorInDirection( int? currentBest, int candidate, ElevatorTravelDirection direction )
	{
		if ( !currentBest.HasValue )
			return candidate;

		return direction == ElevatorTravelDirection.Up
			? candidate < currentBest.Value ? candidate : currentBest
			: candidate > currentBest.Value ? candidate : currentBest;
	}

	private bool IsFloorAhead( int floor, ElevatorTravelDirection direction )
	{
		var offset = GetOffsetForFloor( floor );
		return direction == ElevatorTravelDirection.Up
			? offset > TravelOffset + ArrivalEpsilon
			: offset < TravelOffset - ArrivalEpsilon;
	}

	private int GetNearestFloorForOffset( float offset )
	{
		var bestFloor = MinFloor;
		var bestDistance = float.MaxValue;
		for ( var floor = MinFloor; floor <= SafeFloorCount; floor++ )
		{
			var distance = MathF.Abs( GetOffsetForFloor( floor ) - offset );
			if ( distance >= bestDistance )
				continue;

			bestFloor = floor;
			bestDistance = distance;
		}

		return bestFloor;
	}

	private bool IsAtFloor( int floor )
	{
		return MathF.Abs( GetOffsetForFloor( ClampFloor( floor ) ) - TravelOffset ) <= ArrivalEpsilon;
	}

	private ElevatorTravelDirection NormalizeHallDirection( int floor, ElevatorTravelDirection direction )
	{
		var target = ClampFloor( floor );
		if ( target <= MinFloor )
			return ElevatorTravelDirection.Up;

		if ( target >= SafeFloorCount )
			return ElevatorTravelDirection.Down;

		return direction == ElevatorTravelDirection.Down ? ElevatorTravelDirection.Down :
			direction == ElevatorTravelDirection.Up ? ElevatorTravelDirection.Up :
			ElevatorTravelDirection.Idle;
	}

	private SortedSet<int> GetHallRequestSet( ElevatorTravelDirection direction )
	{
		return direction == ElevatorTravelDirection.Down ? _downHallRequests : _upHallRequests;
	}

	private static ElevatorTravelDirection OppositeDirection( ElevatorTravelDirection direction )
	{
		return direction == ElevatorTravelDirection.Up ? ElevatorTravelDirection.Down :
			direction == ElevatorTravelDirection.Down ? ElevatorTravelDirection.Up :
			ElevatorTravelDirection.Idle;
	}

	private void SyncPendingRequests()
	{
		PendingCabFloors = SerializeFloors( _cabRequests );
		PendingUpHallFloors = SerializeFloors( _upHallRequests );
		PendingDownHallFloors = SerializeFloors( _downHallRequests );
		QueueRevision++;
	}

	private bool CanCallerUseElevator( Connection caller, Vector3? clientWorldPosition = null, Vector3? clientEyePosition = null )
	{
		if ( caller is null )
			return false;

		var player = Player.FindPlayerBySteamId( caller.SteamId.Value );
		if ( !player.IsValid() )
			return false;

		if ( _passengerSteamIds.Contains( caller.SteamId.Value ) || IsPlayerStandingOnPlatform( player ) )
			return true;

		if ( IsPlayerCloseEnoughToUse( player.WorldPosition ) )
			return true;

		if ( player.Controller.IsValid() && IsPlayerCloseEnoughToUse( player.Controller.EyePosition ) )
			return true;

		if ( clientWorldPosition.HasValue
			&& IsTrustedClientUsePosition( player, clientWorldPosition.Value )
			&& IsPlayerCloseEnoughToUse( clientWorldPosition.Value ) )
			return true;

		if ( clientEyePosition.HasValue
			&& IsTrustedClientUsePosition( player, clientEyePosition.Value )
			&& IsPlayerCloseEnoughToUse( clientEyePosition.Value ) )
			return true;

		return false;
	}

	private bool CanCallerUseElevatorAtFloor( Connection caller, int floor, Vector3? clientWorldPosition = null, Vector3? clientEyePosition = null )
	{
		if ( caller is null )
			return false;

		var player = Player.FindPlayerBySteamId( caller.SteamId.Value );
		if ( !player.IsValid() )
			return false;

		var targetFloor = ClampFloor( floor );
		if ( IsPlayerCloseEnoughToUseFloor( player.WorldPosition, targetFloor ) )
			return true;

		if ( player.Controller.IsValid() && IsPlayerCloseEnoughToUseFloor( player.Controller.EyePosition, targetFloor ) )
			return true;

		if ( clientWorldPosition.HasValue
			&& IsTrustedClientUsePosition( player, clientWorldPosition.Value )
			&& IsPlayerCloseEnoughToUseFloor( clientWorldPosition.Value, targetFloor ) )
			return true;

		if ( clientEyePosition.HasValue
			&& IsTrustedClientUsePosition( player, clientEyePosition.Value )
			&& IsPlayerCloseEnoughToUseFloor( clientEyePosition.Value, targetFloor ) )
			return true;

		return false;
	}

	private int ClampFloor( int floor )
	{
		return Math.Clamp( floor, MinFloor, SafeFloorCount );
	}

	private void NormalizeHostFloors()
	{
		CurrentFloor = ClampFloor( CurrentFloor );
		TargetFloor = ClampFloor( TargetFloor );
	}

	private float GetOffsetForFloor( int floor )
	{
		var safeFloor = ClampFloor( floor );
		if ( safeFloor <= MinFloor )
			return 0f;

		if ( !UseCustomFloorHeights )
			return MathF.Max( 0f, FloorHeight ) * (safeFloor - MinFloor);

		var offset = 0f;
		for ( var i = 0; i < safeFloor - MinFloor; i++ )
		{
			var stepHeight = FloorHeights is not null && i < FloorHeights.Count ? FloorHeights[i] : FloorHeight;
			offset += MathF.Max( 0f, stepHeight );
		}

		return offset;
	}

	private GameObject GetPlatform()
	{
		return Platform.IsValid() ? Platform : GameObject;
	}

	private void CacheBasePosition()
	{
		if ( _hasBasePosition )
			return;

		var platform = GetPlatform();
		if ( !platform.IsValid() )
			return;

		_baseLocalPosition = platform.LocalPosition;
		_hasBasePosition = true;
	}

	private void ApplyPlatformPosition()
	{
		CacheBasePosition();

		var platform = GetPlatform();
		if ( !platform.IsValid() || !_hasBasePosition )
			return;

		platform.LocalPosition = _baseLocalPosition + Vector3.Up * TravelOffset;
	}

	private Vector3 GetPlatformMovementDelta( Vector3 previousPlatformPosition )
	{
		var platform = GetPlatform();
		var currentPosition = platform.IsValid() ? platform.WorldPosition : WorldPosition;
		return currentPosition - previousPlatformPosition;
	}

	private void ApplyPlatformSurfaceVelocity( Vector3 delta )
	{
		var platform = GetPlatform();
		if ( !platform.IsValid() )
			return;

		var velocity = Time.Delta > 0f ? delta / Time.Delta : Vector3.Zero;
		var localVelocity = platform.WorldTransform.NormalToLocal( velocity );

		foreach ( var collider in GetPlatformColliders() )
		{
			if ( !collider.IsValid() )
				continue;

			collider.SurfaceVelocity = localVelocity;
		}
	}

	private void RefreshPassengers()
	{
		if ( !CarryPlayers )
		{
			_passengerSteamIds.Clear();
			_passengerLastStandingTimeBySteamId.Clear();
			return;
		}

		foreach ( var player in Scene.GetAllComponents<Player>() )
		{
			if ( !CanCarryPlayer( player ) )
				continue;

			var steamId = GetPlayerSteamId( player );
			if ( steamId == 0L )
				continue;

			if ( IsPlayerStandingOnPlatform( player ) )
			{
				_passengerSteamIds.Add( steamId );
				_passengerLastStandingTimeBySteamId[steamId] = Time.Now;
			}
		}

		var remove = new List<long>();
		foreach ( var steamId in _passengerSteamIds )
		{
			var player = Player.FindPlayerBySteamId( steamId );
			if ( !CanCarryPlayer( player ) )
			{
				remove.Add( steamId );
				continue;
			}

			if ( IsPlayerStandingOnPlatform( player ) )
			{
				_passengerLastStandingTimeBySteamId[steamId] = Time.Now;
				continue;
			}

			if ( IsPlayerNearPlatformDeck( player ) )
			{
				_passengerLastStandingTimeBySteamId[steamId] = Time.Now;
				continue;
			}

			if ( HasPassengerCarryMotion() && IsPlayerHorizontallyNearPlatform( player ) )
			{
				_passengerLastStandingTimeBySteamId[steamId] = Time.Now;
				continue;
			}

			if ( HasPassengerCarryMotion() && HasRecentPlatformContact( steamId ) )
				continue;

			remove.Add( steamId );
		}

		foreach ( var steamId in remove )
		{
			_passengerSteamIds.Remove( steamId );
			_passengerLastStandingTimeBySteamId.Remove( steamId );
		}
	}

	private void CarryPassengers( Vector3 delta )
	{
		if ( !CarryPlayers || delta.LengthSquared <= 0.000001f )
			return;

		foreach ( var steamId in _passengerSteamIds )
		{
			var player = Player.FindPlayerBySteamId( steamId );
			if ( !CanCarryPlayer( player ) )
				continue;

			if ( player.IsProxy )
				continue;

			player.ApplyElevatorCarryDelta( delta );
		}
	}

	private bool ShouldCarryLocalPassenger()
	{
		if ( !CarryPlayers )
			return false;

		var player = Player.Local;
		if ( !CanCarryPlayer( player ) || player.IsProxy )
			return false;

		var steamId = GetPlayerSteamId( player );
		if ( IsPlayerStandingOnPlatform( player ) )
		{
			if ( steamId != 0L )
			{
				_passengerSteamIds.Add( steamId );
				_passengerLastStandingTimeBySteamId[steamId] = Time.Now;
			}

			return true;
		}

		if ( IsPlayerNearPlatformDeck( player ) )
		{
			if ( steamId != 0L )
			{
				_passengerSteamIds.Add( steamId );
				_passengerLastStandingTimeBySteamId[steamId] = Time.Now;
			}

			return true;
		}

		if ( steamId == 0L || !_passengerSteamIds.Contains( steamId ) )
			return false;

		if ( HasPassengerCarryMotion() && IsPlayerHorizontallyNearPlatform( player ) )
		{
			_passengerLastStandingTimeBySteamId[steamId] = Time.Now;
			return true;
		}

		if ( HasPassengerCarryMotion() && HasRecentPlatformContact( steamId ) )
			return true;

		_passengerSteamIds.Remove( steamId );
		_passengerLastStandingTimeBySteamId.Remove( steamId );
		return false;
	}

	private void CarryLocalPassenger( Vector3 delta, bool shouldCarry )
	{
		if ( !shouldCarry || delta.LengthSquared <= 0.000001f )
			return;

		var player = Player.Local;
		if ( !CanCarryPlayer( player ) || player.IsProxy )
			return;

		player.ApplyElevatorCarryDelta( delta );
	}

	private bool CanCarryPlayer( Player player )
	{
		return player.IsValid()
			&& player.Controller.IsValid()
			&& player.IsAlive
			&& !player.IsArrested;
	}

	private bool IsPlayerStandingOnPlatform( Player player )
	{
		if ( !CanCarryPlayer( player ) )
			return false;

		if ( IsPlayerGroundedOnPlatform( player ) )
			return true;

		if ( IsPlayerStandingOnPlatformBounds( player ) )
			return true;

		var body = player.Controller.BodyBox();
		var feetCenter = new Vector3(
			(body.Mins.x + body.Maxs.x) * 0.5f,
			(body.Mins.y + body.Maxs.y) * 0.5f,
			body.Mins.z + 4f );

		var traceDistance = MathF.Max( 8f, PassengerTraceDistance );
		var trace = player.Scene.Trace
			.Ray( feetCenter + Vector3.Up * 8f, feetCenter - Vector3.Up * traceDistance )
			.IgnoreGameObjectHierarchy( player.GameObject )
			.WithoutTags( "player", "bullet" )
			.Run();

		return trace.Hit && IsPlatformObject( trace.GameObject );
	}

	private bool IsPlayerGroundedOnPlatform( Player player )
	{
		if ( !CanCarryPlayer( player ) )
			return false;

		if ( IsPlatformObject( player.Controller.GroundObject ) )
			return true;

		var groundComponent = player.Controller.GroundComponent;
		return groundComponent.IsValid() && IsPlatformObject( groundComponent.GameObject );
	}

	private bool IsPlayerStandingOnPlatformBounds( Player player )
	{
		if ( !TryGetPlatformBounds( out var platformBounds ) )
			return false;

		var playerBounds = player.Controller.BodyBox();
		var feetZ = playerBounds.Mins.z;
		var platformTop = platformBounds.Maxs.z;

		if ( feetZ < platformTop - PassengerFeetBelowPlatformTolerance )
			return false;

		if ( feetZ > platformTop + PassengerFeetAbovePlatformTolerance )
			return false;

		return playerBounds.Maxs.x >= platformBounds.Mins.x
			&& playerBounds.Mins.x <= platformBounds.Maxs.x
			&& playerBounds.Maxs.y >= platformBounds.Mins.y
			&& playerBounds.Mins.y <= platformBounds.Maxs.y;
	}

	private bool IsPlayerHorizontallyNearPlatform( Player player )
	{
		if ( !CanCarryPlayer( player ) )
			return false;

		if ( TryGetPlatformBounds( out var platformBounds ) )
		{
			var body = player.Controller.BodyBox();
			var playerCenter = (body.Mins + body.Maxs) * 0.5f;
			var horizontalDistance = GetHorizontalDistanceToBounds( playerCenter, platformBounds );
			return horizontalDistance <= MathF.Max( 16f, PassengerHorizontalRadius );
		}

		var platform = GetPlatform();
		var platformPosition = platform.IsValid() ? platform.WorldPosition : WorldPosition;
		var delta = player.WorldPosition - platformPosition;
		delta.z = 0f;

		return delta.Length <= MathF.Max( 16f, PassengerHorizontalRadius );
	}

	private bool IsPlayerNearPlatformDeck( Player player )
	{
		if ( !CanCarryPlayer( player ) || !TryGetPlatformBounds( out var platformBounds ) )
			return false;

		var playerBounds = player.Controller.BodyBox();
		var playerCenter = (playerBounds.Mins + playerBounds.Maxs) * 0.5f;
		var horizontalDistance = GetHorizontalDistanceToBounds( playerCenter, platformBounds );
		if ( horizontalDistance > MathF.Max( 16f, PassengerHorizontalRadius ) )
			return false;

		var feetZ = playerBounds.Mins.z;
		var platformTop = platformBounds.Maxs.z;
		if ( feetZ < platformTop - PassengerFeetBelowPlatformTolerance )
			return false;

		return feetZ <= platformTop + PassengerDeckRetentionAboveTolerance;
	}

	private bool HasRecentPlatformContact( long steamId )
	{
		if ( !_passengerLastStandingTimeBySteamId.TryGetValue( steamId, out var lastStandingTime ) )
			return false;

		var graceSeconds = MathF.Max( 0f, PassengerReleaseGraceSeconds );
		return Time.Now - lastStandingTime <= graceSeconds;
	}

	private bool HasPassengerCarryMotion()
	{
		return IsMoving || MathF.Abs( TargetOffset - TravelOffset ) > ArrivalEpsilon;
	}

	private bool IsPlatformObject( GameObject hitObject )
	{
		var platform = GetPlatform();
		if ( !platform.IsValid() || !hitObject.IsValid() )
			return false;

		var go = hitObject;
		while ( go.IsValid() )
		{
			if ( go == platform )
				return true;

			if ( go.Components.TryGet<Elevator>( out var elevator ) && elevator == this )
				return true;

			go = go.Parent;
		}

		return false;
	}

	private bool IsPlayerCloseEnoughToUse( Vector3 position )
	{
		var maxDistance = MathF.Max( 0f, MaxUseDistance );
		if ( maxDistance <= 0f )
			return false;

		if ( TryGetPlatformBounds( out var platformBounds ) )
			return GetDistanceToBounds( position, platformBounds ) <= maxDistance;

		var platform = GetPlatform();
		var checkPosition = platform.IsValid() ? platform.WorldPosition : WorldPosition;
		return Vector3.DistanceBetween( position, checkPosition ) <= maxDistance;
	}

	private bool IsPlayerCloseEnoughToUseFloor( Vector3 position, int floor )
	{
		var maxDistance = MathF.Max( 0f, MaxUseDistance );
		if ( maxDistance <= 0f )
			return false;

		if ( TryGetPlatformBoundsAtFloor( floor, out var platformBounds ) )
			return GetDistanceToBounds( position, platformBounds ) <= maxDistance;

		var platform = GetPlatform();
		var basePosition = platform.IsValid() ? platform.WorldPosition : WorldPosition;
		var floorPosition = basePosition + Vector3.Up * (GetOffsetForFloor( floor ) - TravelOffset);
		return Vector3.DistanceBetween( position, floorPosition ) <= maxDistance;
	}

	private bool IsTrustedClientUsePosition( Player player, Vector3 position )
	{
		if ( !player.IsValid() )
			return false;

		var hostPosition = player.WorldPosition;
		var horizontalDistance = GetHorizontalDistance( position, hostPosition );
		var verticalDistance = MathF.Abs( position.z - hostPosition.z );

		if ( player.Controller.IsValid() )
		{
			var eyePosition = player.Controller.EyePosition;
			horizontalDistance = MathF.Min( horizontalDistance, GetHorizontalDistance( position, eyePosition ) );
			verticalDistance = MathF.Min( verticalDistance, MathF.Abs( position.z - eyePosition.z ) );
		}

		var maxHorizontalDistance = MathF.Max( 96f, MaxUseDistance + 64f );
		if ( horizontalDistance > maxHorizontalDistance )
			return false;

		var maxElevatorTravel = MathF.Abs( GetOffsetForFloor( SafeFloorCount ) );
		var maxVerticalDistance = MathF.Max( MaxUseDistance + 128f, maxElevatorTravel + MaxUseDistance + 128f );
		return verticalDistance <= maxVerticalDistance;
	}

	private bool TryGetPlatformBounds( out BBox bounds )
	{
		bounds = default;

		var foundBounds = false;
		foreach ( var collider in GetPlatformColliders() )
		{
			if ( !collider.IsValid() || collider.IsTrigger )
				continue;

			var colliderBounds = collider.GetWorldBounds();
			bounds = AddBounds( bounds, colliderBounds, ref foundBounds );
		}

		if ( foundBounds )
			return true;

		var platform = GetPlatform();
		if ( !platform.IsValid() )
			return false;

		bounds = platform.GetBounds();
		return true;
	}

	private bool TryGetPlatformBoundsAtFloor( int floor, out BBox bounds )
	{
		if ( !TryGetPlatformBounds( out bounds ) )
			return false;

		var floorDelta = Vector3.Up * (GetOffsetForFloor( floor ) - TravelOffset);
		bounds = new BBox( bounds.Mins + floorDelta, bounds.Maxs + floorDelta );
		return true;
	}

	private IEnumerable<Collider> GetPlatformColliders()
	{
		var platform = GetPlatform();
		if ( !platform.IsValid() )
			yield break;

		foreach ( var collider in platform.Components.GetAll<Collider>( FindMode.EverythingInSelfAndDescendants ) )
			yield return collider;
	}

	private static BBox AddBounds( BBox current, BBox add, ref bool hasBounds )
	{
		if ( !hasBounds )
		{
			hasBounds = true;
			return add;
		}

		current = current.AddPoint( add.Mins );
		current = current.AddPoint( add.Maxs );
		return current;
	}

	private static float GetDistanceToBounds( Vector3 position, BBox bounds )
	{
		var x = MathF.Max( MathF.Max( bounds.Mins.x - position.x, 0f ), position.x - bounds.Maxs.x );
		var y = MathF.Max( MathF.Max( bounds.Mins.y - position.y, 0f ), position.y - bounds.Maxs.y );
		var z = MathF.Max( MathF.Max( bounds.Mins.z - position.z, 0f ), position.z - bounds.Maxs.z );

		return MathF.Sqrt( x * x + y * y + z * z );
	}

	private static float GetHorizontalDistanceToBounds( Vector3 position, BBox bounds )
	{
		var x = MathF.Max( MathF.Max( bounds.Mins.x - position.x, 0f ), position.x - bounds.Maxs.x );
		var y = MathF.Max( MathF.Max( bounds.Mins.y - position.y, 0f ), position.y - bounds.Maxs.y );

		return MathF.Sqrt( x * x + y * y );
	}

	private static float GetHorizontalDistance( Vector3 a, Vector3 b )
	{
		var x = a.x - b.x;
		var y = a.y - b.y;
		return MathF.Sqrt( x * x + y * y );
	}

	private static string SerializeFloors( IEnumerable<int> floors )
	{
		return string.Join( ",", floors );
	}

	private static bool ContainsSerializedFloor( string serializedFloors, int floor )
	{
		if ( string.IsNullOrWhiteSpace( serializedFloors ) )
			return false;

		var floorText = floor.ToString();
		foreach ( var part in serializedFloors.Split( ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries ) )
		{
			if ( part == floorText )
				return true;
		}

		return false;
	}

	private static long GetPlayerSteamId( Player player )
	{
		return player.IsValid()
			? player.GameObject.Network.Owner?.SteamId.Value ?? 0L
			: 0L;
	}

	private void NotifyCaller( Connection caller, string text, NotificationType type )
	{
		if ( caller is null )
			return;

		using ( Rpc.FilterInclude( c => c.SteamId.Value == caller.SteamId.Value ) )
		{
			RpcShowNotification( text, (int)type );
		}
	}

	[Rpc.Broadcast]
	private void RpcShowNotification( string text, int type )
	{
		switch ( (NotificationType)type )
		{
			case NotificationType.Warn:
				Notification.Warn( text, 2.5f );
				break;
			case NotificationType.Error:
				Notification.Error( text, 2.5f );
				break;
			default:
				Notification.Info( text, 2.5f );
				break;
		}
	}

	[Rpc.Broadcast]
	private void RpcPlaySoundAtPlatform( SoundEvent sound )
	{
		if ( !sound.IsValid() )
			return;

		var platform = GetPlatform();
		Sound.Play( sound, platform.IsValid() ? platform.WorldPosition : WorldPosition );
	}
}
