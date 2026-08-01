using Sandbox;
using System;
using System.Collections.Generic;

public sealed partial class QuestNpcMark
{
	private static readonly Dictionary<long, TimeUntil> HitCooldowns = new();
	private const float HitRequestCooldownSeconds = 0.3f;

	partial void RequestInteractServer()
	{
		if ( !Networking.IsHost ) return;

		var caller = Rpc.Caller ?? Connection.Local;
		if ( !TryGetCallerPlayer( caller, out var player ) ) return;
		if ( Vector3.DistanceBetween( player.WorldPosition, WorldPosition ) > MathF.Max( 1f, MaxInteractDistance ) ) return;

		var questDefinition = QuestDatabase.FindQuestById( NormalizedQuestId );
		if ( questDefinition is null )
		{
			Log.Error( $"[Mark] Quest definition '{NormalizedQuestId}' was not found." );
			HostOpenDialogue( caller, MarkDialogueState.Unavailable );
			return;
		}

		if ( !EnsureMarkTasksLoaded( questDefinition ) )
		{
			HostOpenDialogue( caller, MarkDialogueState.Unavailable );
			return;
		}

		var playerQuest = player.Components.Get<PlayerQuest>();
		if ( !playerQuest.IsValid() || !playerQuest.EnsureLoadedFromDisk() )
		{
			HostOpenDialogue( caller, MarkDialogueState.Unavailable );
			return;
		}

		CancelLegacyStarterQuest( playerQuest );

		if ( playerQuest.HasFinishedQuest( questDefinition ) )
		{
			HostOpenDialogue( caller, MarkDialogueState.AlreadyFinished );
			return;
		}

		var activeQuest = playerQuest.GetActiveQuest( questDefinition );
		if ( activeQuest is null )
		{
			if ( !QuestManager.TryGiveQuest( playerQuest, questDefinition ) )
			{
				HostOpenDialogue( caller, MarkDialogueState.Unavailable );
				return;
			}

			activeQuest = playerQuest.GetActiveQuest( questDefinition );
		}

		if ( string.Equals( activeQuest?.CurrentQuestTask?.Id, MarkIntroQuest.MeetTaskId, StringComparison.OrdinalIgnoreCase ) )
		{
			MarkIntroQuest.TryAdvance( player, MarkIntroQuest.MeetTaskId );
			HostOpenDialogue( caller, MarkDialogueState.Introduction, 1, 1 );
			return;
		}

		var taskId = activeQuest.CurrentQuestTask?.Id ?? string.Empty;
		if ( string.Equals( taskId, MarkIntroQuest.ReturnTaskId, StringComparison.OrdinalIgnoreCase ) )
		{
			HostOpenDialogue( caller, MarkDialogueState.ReturnReady, activeQuest.CurrentCount, activeQuest.CurrentQuestTask?.Count ?? 1 );
			return;
		}

		OpenCurrentObjective( caller, activeQuest );
	}

	partial void RequestDialogueActionServer( int action )
	{
		if ( !Networking.IsHost ) return;

		var caller = Rpc.Caller ?? Connection.Local;
		if ( !TryGetCallerPlayer( caller, out var player ) ) return;
		if ( Vector3.DistanceBetween( player.WorldPosition, WorldPosition ) > MathF.Max( 1f, MaxInteractDistance ) ) return;

		var definition = QuestDatabase.FindQuestById( NormalizedQuestId );
		var playerQuest = player.Components.Get<PlayerQuest>();
		if ( definition is null || !EnsureMarkTasksLoaded( definition ) || !playerQuest.IsValid() || !playerQuest.EnsureLoadedFromDisk() )
		{
			HostOpenDialogue( caller, MarkDialogueState.Unavailable );
			return;
		}

		if ( (MarkDialogueAction)action == MarkDialogueAction.AcceptQuest )
		{
			if ( playerQuest.HasFinishedQuest( definition ) )
			{
				HostOpenDialogue( caller, MarkDialogueState.AlreadyFinished );
				return;
			}

			var active = playerQuest.GetActiveQuest( definition );
			if ( active is null && !QuestManager.TryGiveQuest( playerQuest, definition ) )
			{
				HostOpenDialogue( caller, MarkDialogueState.Unavailable );
				return;
			}

			active = playerQuest.GetActiveQuest( definition );
			HostOpenDialogue( caller, MarkDialogueState.QuestStarted, active?.CurrentCount ?? 0, active?.CurrentQuestTask?.Count ?? 1 );
			return;
		}

		if ( (MarkDialogueAction)action == MarkDialogueAction.TurnInQuest )
		{
			var active = playerQuest.GetActiveQuest( definition );
			if ( active is null || !string.Equals( active.CurrentQuestTask?.Id, MarkIntroQuest.ReturnTaskId, StringComparison.OrdinalIgnoreCase ) )
			{
				if ( active is null ) HostOpenDialogue( caller, MarkDialogueState.AlreadyFinished );
				else OpenCurrentObjective( caller, active );
				return;
			}

			if ( QuestManager.TryAdvanceCount( playerQuest, definition ) )
				HostOpenDialogue( caller, MarkDialogueState.Completed, 1, 1 );
			else
				HostOpenDialogue( caller, MarkDialogueState.Unavailable );
		}
	}

	partial void RequestTrainingHitServer( Vector3 origin, Vector3 direction )
	{
		if ( !Networking.IsHost ) return;

		var caller = Rpc.Caller ?? Connection.Local;
		if ( !TryGetCallerPlayer( caller, out var player ) ) return;
		if ( !player.IsAlive ) return;

		var steamId = caller.SteamId.Value;
		if ( HitCooldowns.TryGetValue( steamId, out var cooldown ) && !cooldown ) return;

		var equippedItem = !string.IsNullOrWhiteSpace( player.EquippedWeaponItemId )
			? player.EquippedWeaponItemId
			: player.CurrentWeaponItemId;
		if ( !string.Equals( equippedItem, "hands", StringComparison.OrdinalIgnoreCase ) ) return;

		var maxDistance = MathF.Max( 1f, MaxHitDistance );
		if ( Vector3.DistanceBetween( player.WorldPosition, WorldPosition ) > maxDistance + 32f ) return;
		if ( !player.Controller.IsValid() ) return;

		var eye = player.Controller.EyeTransform;
		if ( Vector3.DistanceBetween( eye.Position, origin ) > 64f ) return;
		if ( direction.LengthSquared <= 0.001f ) return;

		direction = direction.Normal;
		if ( Vector3.Dot( eye.Forward.Normal, direction ) < 0.5f ) return;

		var trace = Scene.Trace
			.Ray( origin, origin + direction * maxDistance )
			.IgnoreGameObjectHierarchy( player.GameObject )
			.Run();
		if ( !trace.Hit || !TraceHitThisMark( trace.GameObject ) ) return;

		if ( !MarkIntroQuest.TryAdvance( player, MarkIntroQuest.HitTaskId ) ) return;
		HitCooldowns[steamId] = HitRequestCooldownSeconds;
	}

	private string NormalizedQuestId => string.IsNullOrWhiteSpace( QuestId ) ? DefaultQuestId : QuestId.Trim();

	private static bool EnsureMarkTasksLoaded( QuestDefinition definition )
	{
		return MarkIntroQuest.EnsureDefinitionTasks( definition );
	}

	private bool TraceHitThisMark( GameObject hitObject )
	{
		var current = hitObject;
		while ( current.IsValid() )
		{
			if ( current == GameObject ) return true;
			if ( current.Components.TryGet<QuestNpcMark>( out var mark ) && mark == this ) return true;
			current = current.Parent;
		}

		return false;
	}

	private static bool TryGetCallerPlayer( Connection caller, out Player player )
	{
		player = null;
		if ( caller is null ) return false;

		player = Player.FindPlayerBySteamId( caller.SteamId.Value );
		return player.IsValid() && player.GameObject.Network.Owner == caller;
	}

	private void OpenCurrentObjective( Connection caller, Quest activeQuest )
	{
		var taskId = activeQuest?.CurrentQuestTask?.Id ?? string.Empty;
		var current = activeQuest?.CurrentCount ?? 0;
		var total = activeQuest?.CurrentQuestTask?.Count ?? 1;
		if ( string.Equals( taskId, MarkIntroQuest.HitTaskId, StringComparison.OrdinalIgnoreCase ) )
		{
			HostOpenDialogue( caller, MarkDialogueState.HitObjective, current, total );
			return;
		}

		if ( string.Equals( taskId, MarkIntroQuest.BuyDoorsTaskId, StringComparison.OrdinalIgnoreCase ) )
		{
			HostOpenDialogue( caller, MarkDialogueState.DoorsObjective, current, total );
			return;
		}

		if ( string.Equals( taskId, MarkIntroQuest.SpawnPropTaskId, StringComparison.OrdinalIgnoreCase ) )
		{
			HostOpenDialogue( caller, MarkDialogueState.SpawnPropObjective, current, total );
			return;
		}

		if ( string.Equals( taskId, MarkIntroQuest.PhysgunPropTaskId, StringComparison.OrdinalIgnoreCase ) )
		{
			HostOpenDialogue( caller, MarkDialogueState.PhysgunObjective, current, total );
			return;
		}

		if ( string.Equals( taskId, MarkIntroQuest.RemovePropTaskId, StringComparison.OrdinalIgnoreCase ) )
		{
			HostOpenDialogue( caller, MarkDialogueState.RemovePropObjective, current, total );
			return;
		}

		HostOpenDialogue( caller, MarkDialogueState.Unavailable );
	}

	private static void CancelLegacyStarterQuest( PlayerQuest playerQuest )
	{
		var legacyQuest = QuestDatabase.FindQuestById( "q1" );
		if ( legacyQuest is null || !playerQuest.HasActiveQuest( legacyQuest ) ) return;

		QuestManager.TryCancelQuest( playerQuest, legacyQuest );
	}

	private void HostOpenDialogue( Connection connection, MarkDialogueState state, int currentCount = 0, int requiredCount = 1 )
	{
		if ( !Networking.IsHost || connection is null ) return;

		using ( Rpc.FilterInclude( c => c.SteamId.Value == connection.SteamId.Value ) )
		{
			RpcOpenDialogue( (int)state, Math.Max( 0, currentCount ), Math.Max( 1, requiredCount ) );
		}
	}
}
