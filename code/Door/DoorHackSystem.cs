using Sandbox;
using System;
using System.Collections.Generic;

public sealed class DoorHackSystem : Component
{
	private const float SuccessCooldownSeconds = 15f;
	private const float FailedCooldownSeconds = 120f;
	private const float SuccessGraceSeconds = 0.35f;
	private const float TimeoutGraceSeconds = 1.0f;

	private static readonly Dictionary<long, ActiveHackAttempt> ActiveAttemptsBySteamId = new();
	private static int NextAttemptId = 1;

	public static void HostRequestStartHackFromRpc( GameObject targetObject )
	{
		if ( !Networking.IsHost )
			return;

		var caller = Rpc.Caller ?? Connection.Local;
		if ( caller is null )
			return;

		var player = Player.FindPlayerBySteamId( caller.SteamId.Value );
		if ( !player.IsValid() )
		{
			NotifyLockpicker( caller, GameLocalization.Phrase( "notify.player.not_ready", "Your player is not ready." ), NotificationType.Warn, 2.5f );
			return;
		}

		if ( !targetObject.IsValid() || !TryGetHackable( targetObject, out var hackable ) )
		{
			NotifyLockpicker( caller, GameLocalization.Phrase( "notify.lockpick.cannot_lockpick", "This door cannot be lockpicked." ), NotificationType.Warn, 3.0f );
			return;
		}

		if ( player.IsArrested )
		{
			NotifyLockpicker( caller, GameLocalization.Phrase( "notify.player.arrested", "You are arrested." ), NotificationType.Warn, 2.5f );
			return;
		}

		if ( player.LockpickCooldown > 0f )
		{
			var secondsLeft = (float)player.LockpickCooldown;
			NotifyLockpicker( caller, GameLocalization.Format( "notify.lockpick.wait", "Wait {0:0}s before the next attempt.", secondsLeft ), NotificationType.Warn, 2.5f );
			return;
		}

		var steamId = caller.SteamId.Value;
		if ( ActiveAttemptsBySteamId.ContainsKey( steamId ) )
		{
			NotifyLockpicker( caller, GameLocalization.Phrase( "notify.lockpick.already_active", "Lockpick already in progress." ), NotificationType.Warn, 2.0f );
			return;
		}

		if ( !hackable.CanBeDoorHacked( player ) )
		{
			NotifyLockpicker( caller, GameLocalization.Phrase( "notify.lockpick.cannot_lockpick", "This door cannot be lockpicked." ), NotificationType.Warn, 3.0f );
			return;
		}

		if ( !IsPlayerInHackRange( player, hackable ) )
		{
			NotifyLockpicker( caller, GameLocalization.Phrase( "notify.lockpick.too_far", "Too far from the door." ), NotificationType.Warn, 2.5f );
			return;
		}

		var squareCount = DoorHackMinigame.GetConfiguredSquareCount();
		var instructionSeconds = DoorHackMinigame.GetConfiguredInstructionSeconds();
		var gameSeconds = DoorHackMinigame.GetConfiguredGameSeconds();

		var attempt = new ActiveHackAttempt
		{
			Id = NextAttemptId++,
			SteamId = steamId,
			TargetObject = targetObject,
			InstructionUntil = instructionSeconds,
			SuccessUntil = instructionSeconds + gameSeconds + SuccessGraceSeconds
		};

		if ( NextAttemptId == int.MaxValue )
			NextAttemptId = 1;

		ActiveAttemptsBySteamId[steamId] = attempt;

		using ( Rpc.FilterInclude( x => x.SteamId.Value == steamId ) )
		{
			RpcStartHackMinigame( attempt.Id, squareCount, instructionSeconds, gameSeconds );
		}

		_ = FailAttemptOnTimeout( steamId, attempt.Id, instructionSeconds + gameSeconds + TimeoutGraceSeconds );
	}

	[Rpc.Host]
	public static void RpcSubmitHackResult( int attemptId, bool completed )
	{
		if ( !Networking.IsHost )
			return;

		var caller = Rpc.Caller ?? Connection.Local;
		if ( caller is null )
			return;

		var steamId = caller.SteamId.Value;
		if ( !ActiveAttemptsBySteamId.TryGetValue( steamId, out var attempt ) )
			return;

		if ( attempt.Id != attemptId )
			return;

		var success = completed && attempt.InstructionUntil && !attempt.SuccessUntil;
		FinishAttempt( caller, attempt, success );
	}

	[Rpc.Broadcast]
	private static void RpcStartHackMinigame( int attemptId, int squareCount, float instructionSeconds, float gameSeconds )
	{
		if ( !Networking.IsHost && Rpc.Caller is not null && !Rpc.Caller.IsHost )
			return;

		DoorHackMinigame.Open( attemptId, squareCount, instructionSeconds, gameSeconds );
	}

	private static async System.Threading.Tasks.Task FailAttemptOnTimeout( long steamId, int attemptId, float seconds )
	{
		await System.Threading.Tasks.Task.Delay( TimeSpan.FromSeconds( MathF.Max( 0.1f, seconds ) ) );

		if ( !Networking.IsHost )
			return;

		if ( !ActiveAttemptsBySteamId.TryGetValue( steamId, out var attempt ) || attempt.Id != attemptId )
			return;

		var connection = FindConnectionBySteamId( steamId );
		FinishAttempt( connection, attempt, false );
	}

	private static void FinishAttempt( Connection caller, ActiveHackAttempt attempt, bool success )
	{
		ActiveAttemptsBySteamId.Remove( attempt.SteamId );

		var player = Player.FindPlayerBySteamId( attempt.SteamId );
		if ( !player.IsValid() )
			return;

		if ( !attempt.TargetObject.IsValid() || !TryGetHackable( attempt.TargetObject, out var hackable ) )
		{
			player.LockpickCooldown = FailedCooldownSeconds;
			NotifyLockpicker( caller, GameLocalization.Format( "notify.lockpick.failed", "Lockpick failed. Next attempt in {0:0}s.", FailedCooldownSeconds ), NotificationType.Error, 3.5f );
			return;
		}

		if ( success && hackable.CanBeDoorHacked( player ) && IsPlayerInHackRange( player, hackable ) )
		{
			player.LockpickCooldown = SuccessCooldownSeconds;
			hackable.HostOnDoorHackSucceeded( player );
			NotifyLockpicker( caller, GameLocalization.Format( "notify.lockpick.success", "Lockpick succeeded! Door opened. Next attempt in {0:0}s.", SuccessCooldownSeconds ), NotificationType.Info, 3.5f );
			return;
		}

		player.LockpickCooldown = FailedCooldownSeconds;
		hackable.HostOnDoorHackFailed( player );
		NotifyLockpicker( caller, GameLocalization.Format( "notify.lockpick.failed", "Lockpick failed. Next attempt in {0:0}s.", FailedCooldownSeconds ), NotificationType.Error, 3.5f );
	}

	public static bool TryGetHackable( GameObject targetObject, out IDoorHackable hackable )
	{
		hackable = null;
		return targetObject.IsValid() && targetObject.Components.TryGet( out hackable, FindMode.EverythingInSelfAndAncestors );
	}

	private static bool IsPlayerInHackRange( Player player, IDoorHackable hackable )
	{
		if ( !player.IsValid() || hackable is null )
			return false;

		return Vector3.DistanceBetween( player.WorldPosition, hackable.DoorHackWorldPosition ) <= MathF.Max( 1f, hackable.DoorHackInteractRange );
	}

	private static Connection FindConnectionBySteamId( long steamId )
	{
		foreach ( var connection in Connection.All )
		{
			if ( connection is not null && connection.SteamId.Value == steamId )
				return connection;
		}

		return null;
	}

	private static void NotifyLockpicker( Connection target, string text, NotificationType type, float aliveSeconds )
	{
		if ( target is null )
			return;

		using ( Rpc.FilterInclude( c => c.SteamId.Value == target.SteamId.Value ) )
		{
			RpcShowLockpickNotification( text, (int)type, aliveSeconds );
		}
	}

	[Rpc.Broadcast]
	private static void RpcShowLockpickNotification( string text, int type, float aliveSeconds )
	{
		var notificationType = (NotificationType)type;
		switch ( notificationType )
		{
			case NotificationType.Warn: Notification.Warn( text, aliveSeconds ); break;
			case NotificationType.Error: Notification.Error( text, aliveSeconds ); break;
			default: Notification.Info( text, aliveSeconds ); break;
		}
	}

	private sealed class ActiveHackAttempt
	{
		public int Id { get; init; }
		public long SteamId { get; init; }
		public GameObject TargetObject { get; init; }
		public TimeUntil InstructionUntil { get; init; }
		public TimeUntil SuccessUntil { get; init; }
	}
}
