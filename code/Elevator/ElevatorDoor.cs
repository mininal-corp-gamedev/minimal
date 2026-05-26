using Sandbox;
using System;

public sealed class ElevatorDoor : Component
{
	[Property, Group( "Elevator" )] public Elevator TargetElevator { get; set; }
	[Property, Group( "Elevator" )] public bool MatchAnyFloor { get; set; }
	[Property, Group( "Elevator" ), ShowIf( "MatchAnyFloor", false )] public int Floor { get; set; } = 1;
	[Property, Group( "Elevator" )] public float ElevatorSearchRadius { get; set; } = 2048f;

	[Property, Group( "Panels" )] public GameObject LeftDoor { get; set; }
	[Property, Group( "Panels" )] public GameObject RightDoor { get; set; }
	[Property, Group( "Panels" )] public Vector3 LeftOpenOffset { get; set; } = Vector3.Right * -24f;
	[Property, Group( "Panels" )] public Vector3 RightOpenOffset { get; set; } = Vector3.Right * 24f;

	[Property, Group( "Motion" )] public float OpenSpeed { get; set; } = 96f;
	[Property, Group( "Motion" )] public float CloseSpeed { get; set; } = 112f;

	private Vector3 _baseLocalPosition;
	private Vector3 _leftBaseLocalPosition;
	private Vector3 _rightBaseLocalPosition;
	private bool _hasBasePositions;

	protected override void OnStart()
	{
		CacheBasePositions();
		ApplyDoorState( true );
	}

	protected override void OnFixedUpdate()
	{
		CacheBasePositions();
		ApplyDoorState( false );
	}

	private void CacheBasePositions()
	{
		if ( _hasBasePositions )
			return;

		_baseLocalPosition = GameObject.LocalPosition;

		if ( LeftDoor.IsValid() )
			_leftBaseLocalPosition = LeftDoor.LocalPosition;

		if ( RightDoor.IsValid() )
			_rightBaseLocalPosition = RightDoor.LocalPosition;

		_hasBasePositions = true;
	}

	private void ApplyDoorState( bool instant )
	{
		var elevator = GetElevator();
		var shouldOpen = ShouldOpen( elevator );
		var speed = shouldOpen ? OpenSpeed : CloseSpeed;

		if ( LeftDoor.IsValid() || RightDoor.IsValid() )
		{
			MovePanel( LeftDoor, _leftBaseLocalPosition, LeftOpenOffset, shouldOpen, speed, instant );
			MovePanel( RightDoor, _rightBaseLocalPosition, RightOpenOffset, shouldOpen, speed, instant );
			return;
		}

		MovePanel( GameObject, _baseLocalPosition, LeftOpenOffset, shouldOpen, speed, instant );
	}

	private Elevator GetElevator()
	{
		if ( TargetElevator.IsValid() )
			return TargetElevator;

		var elevator = Components.Get<Elevator>( FindMode.EverythingInSelfAndAncestors );
		if ( elevator.IsValid() )
			return elevator;

		return FindClosestElevator();
	}

	private Elevator FindClosestElevator()
	{
		var maxDistance = MathF.Max( 0f, ElevatorSearchRadius );
		if ( maxDistance <= 0f )
			return null;

		Elevator closest = null;
		var closestDistance = maxDistance;
		foreach ( var elevator in Scene.GetAllComponents<Elevator>() )
		{
			if ( !elevator.IsValid() )
				continue;

			var distance = Vector3.DistanceBetween( GameObject.WorldPosition, elevator.GameObject.WorldPosition );
			if ( distance > closestDistance )
				continue;

			closest = elevator;
			closestDistance = distance;
		}

		return closest;
	}

	private bool ShouldOpen( Elevator elevator )
	{
		if ( !elevator.IsValid() )
			return false;

		return MatchAnyFloor
			? elevator.DoorsOpen
			: elevator.IsDoorOpenAtFloor( Floor );
	}

	private static void MovePanel( GameObject panel, Vector3 baseLocalPosition, Vector3 openOffset, bool shouldOpen, float speed, bool instant )
	{
		if ( !panel.IsValid() )
			return;

		var target = baseLocalPosition + (shouldOpen ? openOffset : Vector3.Zero);
		if ( instant )
		{
			panel.LocalPosition = target;
			return;
		}

		panel.LocalPosition = MoveTowards( panel.LocalPosition, target, MathF.Max( 1f, speed ) * Time.Delta );
	}

	private static Vector3 MoveTowards( Vector3 current, Vector3 target, float maxDistanceDelta )
	{
		var delta = target - current;
		var distance = delta.Length;
		if ( distance <= maxDistanceDelta || distance <= 0.001f )
			return target;

		return current + delta / distance * maxDistanceDelta;
	}
}
