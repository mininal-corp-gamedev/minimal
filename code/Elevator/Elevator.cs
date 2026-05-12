using Sandbox;
using System;
using System.Collections.Generic;

public sealed class Elevator : Component
{
	private const int MinFloor = 1;
	private const int MaxFloorCount = 20;
	private const float ArrivalEpsilon = 0.1f;

	[Property, Group( "Setup" )] public GameObject Platform { get; set; }
	[Property, Group( "Setup" )] public int FloorCount { get; set; } = 5;
	[Property, Group( "Setup" )] public int InitialFloor { get; set; } = 1;
	[Property, Group( "Setup" )] public float FloorHeight { get; set; } = 128f;
	[Property, Group( "Setup" )] public bool UseCustomFloorHeights { get; set; } = false;
	[Property, Group( "Setup" ), ShowIf( "UseCustomFloorHeights", true )]
	public List<float> FloorHeights { get; set; } = new() { 128f, 128f, 128f, 128f };

	[Property, Group( "Movement" )] public float MoveSpeed { get; set; } = 128f;
	[Property, Group( "Gameplay" )] public float MaxUseDistance { get; set; } = 180f;
	[Property, Group( "Gameplay" )] public bool RejectRequestsWhileMoving { get; set; } = false;
	[Property, Group( "Passenger Carry" )] public bool CarryPlayers { get; set; } = true;
	[Property, Group( "Passenger Carry" )] public float PassengerTraceDistance { get; set; } = 64f;
	[Property, Group( "Passenger Carry" )] public float PassengerHorizontalRadius { get; set; } = 160f;
	[Property, Group( "Passenger Carry" )] public float PassengerReleaseGraceSeconds { get; set; } = 0.28f;

	[Property, Group( "Sounds" )] public SoundEvent StartSound { get; set; }
	[Property, Group( "Sounds" )] public SoundEvent ArriveSound { get; set; }

	[Sync( SyncFlags.FromHost )] public int CurrentFloor { get; private set; } = 1;
	[Sync( SyncFlags.FromHost )] public int TargetFloor { get; private set; } = 1;
	[Sync( SyncFlags.FromHost )] public float TravelOffset { get; private set; }
	[Sync( SyncFlags.FromHost )] public bool IsMoving { get; private set; }

	public int SafeFloorCount => Math.Clamp( FloorCount, MinFloor, MaxFloorCount );
	public float TargetOffset => GetOffsetForFloor( TargetFloor );
	public float CurrentOffset => TravelOffset;
	public bool IsIdle => !IsMoving;

	private Vector3 _baseLocalPosition;
	private bool _hasBasePosition;
	private readonly HashSet<long> _passengerSteamIds = new();
	private readonly Dictionary<long, float> _passengerLastStandingTimeBySteamId = new();

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
		if ( !Networking.IsHost )
		{
			ApplyPlatformPosition();
			return;
		}

		CacheBasePosition();
		NormalizeHostFloors();
		RefreshPassengers();

		var platform = GetPlatform();
		var previousPlatformPosition = platform.IsValid() ? platform.WorldPosition : WorldPosition;
		var targetOffset = GetOffsetForFloor( TargetFloor );
		var diff = targetOffset - TravelOffset;
		var speed = MathF.Max( 1f, MoveSpeed );
		var step = speed * Time.Delta;

		if ( MathF.Abs( diff ) <= MathF.Max( ArrivalEpsilon, step ) )
		{
			var wasMoving = IsMoving;

			TravelOffset = targetOffset;
			CurrentFloor = TargetFloor;
			IsMoving = false;
			ApplyPlatformPosition();
			CarryPassengers( GetPlatformMovementDelta( previousPlatformPosition ) );

			if ( wasMoving && ArriveSound.IsValid() )
				RpcPlaySoundAtPlatform( ArriveSound );

			return;
		}

		IsMoving = true;
		TravelOffset += MathF.Sign( diff ) * step;
		ApplyPlatformPosition();
		CarryPassengers( GetPlatformMovementDelta( previousPlatformPosition ) );
	}

	public bool CanRequestFloor( int floor )
	{
		if ( floor < MinFloor || floor > SafeFloorCount )
			return false;

		if ( RejectRequestsWhileMoving && IsMoving )
			return false;

		return floor != TargetFloor;
	}

	[Rpc.Host]
	public void RpcRequestFloor( int floor )
	{
		if ( !Networking.IsHost )
			return;

		var caller = Rpc.Caller;
		if ( caller is null )
			return;

		if ( !CanCallerUseElevator( caller ) )
		{
			NotifyCaller( caller, GameLocalization.Phrase( "notify.elevator.too_far", "Too far from elevator." ), NotificationType.Warn );
			return;
		}

		HostSetTargetFloor( floor );
	}

	public void HostSetTargetFloor( int floor )
	{
		if ( !Networking.IsHost )
			return;

		if ( !CanRequestFloor( floor ) )
			return;

		var target = ClampFloor( floor );
		var targetOffset = GetOffsetForFloor( target );
		if ( MathF.Abs( targetOffset - TravelOffset ) <= ArrivalEpsilon )
		{
			TravelOffset = targetOffset;
			CurrentFloor = target;
			TargetFloor = target;
			IsMoving = false;
			ApplyPlatformPosition();
			return;
		}

		TargetFloor = target;
		IsMoving = true;

		if ( StartSound.IsValid() )
			RpcPlaySoundAtPlatform( StartSound );
	}

	private bool CanCallerUseElevator( Connection caller )
	{
		if ( caller is null )
			return false;

		var player = Player.FindPlayerBySteamId( caller.SteamId.Value );
		if ( !player.IsValid() )
			return false;

		if ( _passengerSteamIds.Contains( caller.SteamId.Value ) || IsPlayerStandingOnPlatform( player ) )
			return true;

		var platform = GetPlatform();
		var checkPosition = platform.IsValid() ? platform.WorldPosition : WorldPosition;
		return Vector3.DistanceBetween( player.WorldPosition, checkPosition ) <= MathF.Max( 0f, MaxUseDistance );
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

			if ( IsMoving && IsPlayerHorizontallyNearPlatform( player ) )
			{
				_passengerLastStandingTimeBySteamId[steamId] = Time.Now;
				continue;
			}

			if ( IsMoving && HasRecentPlatformContact( steamId ) )
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

			player.ApplyElevatorCarryDelta( delta );
		}
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

	private bool IsPlayerHorizontallyNearPlatform( Player player )
	{
		if ( !CanCarryPlayer( player ) )
			return false;

		var platform = GetPlatform();
		var platformPosition = platform.IsValid() ? platform.WorldPosition : WorldPosition;
		var delta = player.WorldPosition - platformPosition;
		delta.z = 0f;

		return delta.Length <= MathF.Max( 16f, PassengerHorizontalRadius );
	}

	private bool HasRecentPlatformContact( long steamId )
	{
		if ( !_passengerLastStandingTimeBySteamId.TryGetValue( steamId, out var lastStandingTime ) )
			return false;

		var graceSeconds = MathF.Max( 0f, PassengerReleaseGraceSeconds );
		return Time.Now - lastStandingTime <= graceSeconds;
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
