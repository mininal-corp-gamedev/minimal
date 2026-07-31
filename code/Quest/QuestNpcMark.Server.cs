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
			HostNotify( caller, "Марк пока не может выдать задание.", false );
			return;
		}

		if ( !EnsureMarkTasksLoaded( questDefinition ) )
		{
			HostNotify( caller, "Марк пока не может выдать задание: этапы обучения не загрузились.", false );
			return;
		}

		var playerQuest = player.Components.Get<PlayerQuest>();
		if ( !playerQuest.IsValid() || !playerQuest.EnsureLoadedFromDisk() )
		{
			HostNotify( caller, "Квесты игрока ещё не загрузились.", false );
			return;
		}

		CancelLegacyStarterQuest( playerQuest );

		if ( playerQuest.HasFinishedQuest( questDefinition ) )
		{
			HostNotify( caller, "Марк: Ты уже освоил самые основы. Скоро у меня появятся новые поручения.", true );
			return;
		}

		var activeQuest = playerQuest.GetActiveQuest( questDefinition );
		if ( activeQuest is null )
		{
			if ( !QuestManager.TryGiveQuest( playerQuest, questDefinition ) )
			{
				HostNotify( caller, "Марк не смог выдать задание. Попробуй ещё раз.", false );
				return;
			}

			HostNotify( caller, "Марк: Для начала покажи, что умеешь постоять за себя. Ударь меня кулаками 3 раза.", true );
			return;
		}

		var taskId = activeQuest.CurrentQuestTask?.Id ?? string.Empty;
		if ( string.Equals( taskId, MarkIntroQuest.ReturnTaskId, StringComparison.OrdinalIgnoreCase ) )
		{
			QuestManager.TryAdvanceCount( playerQuest, questDefinition );
			return;
		}

		HostNotify( caller, CurrentObjectiveMessage( taskId ), true );
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
		if ( definition.QuestTasks is { Count: > 0 } ) return true;

		var hit = QuestDatabase.FindTaskById( MarkIntroQuest.HitTaskId );
		var buyDoors = QuestDatabase.FindTaskById( MarkIntroQuest.BuyDoorsTaskId );
		var returnToMark = QuestDatabase.FindTaskById( MarkIntroQuest.ReturnTaskId );
		if ( hit is null || buyDoors is null || returnToMark is null )
		{
			Log.Error( $"[Mark] Task resources failed to load: hit={hit != null}, doors={buyDoors != null}, return={returnToMark != null}." );
			return false;
		}

		definition.QuestTasks = new() { hit, buyDoors, returnToMark };
		Log.Warning( "[Mark] Quest task references were empty and have been restored from the resource database." );
		return true;
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

	private static string CurrentObjectiveMessage( string taskId )
	{
		if ( string.Equals( taskId, MarkIntroQuest.HitTaskId, StringComparison.OrdinalIgnoreCase ) )
			return "Марк: Возьми кулаки и ударь меня 3 раза.";
		if ( string.Equals( taskId, MarkIntroQuest.BuyDoorsTaskId, StringComparison.OrdinalIgnoreCase ) )
			return "Марк: Найди любое свободное жильё и купи любые 2 двери.";

		return "Марк: Посмотри текущее задание в интерфейсе.";
	}

	private static void CancelLegacyStarterQuest( PlayerQuest playerQuest )
	{
		var legacyQuest = QuestDatabase.FindQuestById( "q1" );
		if ( legacyQuest is null || !playerQuest.HasActiveQuest( legacyQuest ) ) return;

		QuestManager.TryCancelQuest( playerQuest, legacyQuest );
	}

	internal static void HostNotify( Connection connection, string message, bool positive )
	{
		if ( !Networking.IsHost || connection is null ) return;

		using ( Rpc.FilterInclude( c => c.SteamId.Value == connection.SteamId.Value ) )
		{
			RpcReceiveMarkMessage( message, positive );
		}
	}
}
