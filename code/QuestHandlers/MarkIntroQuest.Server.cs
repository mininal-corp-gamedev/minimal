using Ambi.Storage;
using Sandbox;
using System;
using System.Linq;

/// <summary>Server-side identifiers and progress entry points for Mark's tutorial.</summary>
public static class MarkIntroQuest
{
	public const string QuestId = "mark_intro";
	public const string MeetTaskId = "mark_meet";
	public const string HitTaskId = "mark_hit";
	public const string BuyDoorsTaskId = "mark_buy_doors";
	public const string SpawnPropTaskId = "mark_spawn_prop";
	public const string PhysgunPropTaskId = "mark_physgun_prop";
	public const string RemovePropTaskId = "mark_remove_prop";
	public const string ReturnTaskId = "mark_return";

	public static bool EnsureStarterQuest( PlayerQuest playerQuest )
	{
		if ( !Networking.IsHost || !playerQuest.IsValid() ) return false;

		var definition = QuestDatabase.FindQuestById( QuestId );
		if ( definition is null || !EnsureDefinitionTasks( definition ) ) return false;
		if ( playerQuest.HasActiveQuest( definition ) || playerQuest.HasFinishedQuest( definition ) ) return true;

		var legacyQuest = QuestDatabase.FindQuestById( "q1" );
		if ( legacyQuest is not null && playerQuest.HasActiveQuest( legacyQuest ) )
			QuestManager.TryCancelQuest( playerQuest, legacyQuest );

		return QuestManager.TryGiveQuest( playerQuest, definition );
	}

	public static bool EnsureDefinitionTasks( QuestDefinition definition )
	{
		if ( definition is null ) return false;

		var taskIds = new[]
		{
			MeetTaskId,
			HitTaskId,
			BuyDoorsTaskId,
			SpawnPropTaskId,
			PhysgunPropTaskId,
			RemovePropTaskId,
			ReturnTaskId
		};

		if ( definition.QuestTasks is { Count: 7 }
			&& taskIds.Select( ( id, index ) => string.Equals( definition.QuestTasks[index]?.Id, id, StringComparison.OrdinalIgnoreCase ) ).All( matches => matches ) )
			return true;

		var tasks = taskIds.Select( QuestDatabase.FindTaskById ).ToList();
		if ( tasks.Any( task => task is null ) )
		{
			Log.Error( $"[Mark] Tutorial task resources are incomplete: {string.Join( ", ", taskIds.Where( ( _, index ) => tasks[index] is null ) )}." );
			return false;
		}

		definition.QuestTasks = tasks;
		return true;
	}

	public static void ApplyInventoryUnlocks( PlayerQuest playerQuest )
	{
		if ( !Networking.IsHost || !playerQuest.IsValid() ) return;

		var player = playerQuest.GameObject.Components.Get<Player>( FindMode.EverythingInSelfAndParent );
		var definition = QuestDatabase.FindQuestById( QuestId );
		if ( !player.IsValid() || player.Inventory is null || definition is null || !EnsureDefinitionTasks( definition ) ) return;

		var finished = playerQuest.HasFinishedQuest( definition );
		var active = playerQuest.GetActiveQuest( definition );
		var currentIndex = active?.CurrentTaskIndex ?? -1;

		SetTutorialItemAvailable( player, "hands", true );
		// Each tool is unlocked before the first objective that requires it.
		SetTutorialItemAvailable( player, "keys", finished || currentIndex >= TaskIndex( definition, BuyDoorsTaskId ) );
		SetTutorialItemAvailable( player, "physgun", finished || currentIndex >= TaskIndex( definition, PhysgunPropTaskId ) );
		SetTutorialItemAvailable( player, "toolgun", finished || currentIndex >= TaskIndex( definition, RemovePropTaskId ) );
	}

	private static int TaskIndex( QuestDefinition definition, string taskId )
	{
		return definition?.QuestTasks?.FindIndex( task => string.Equals( task?.Id, taskId, StringComparison.OrdinalIgnoreCase ) ) ?? int.MaxValue;
	}

	private static void SetTutorialItemAvailable( Player player, string itemId, bool available )
	{
		var count = player.Inventory.GetTotalCount( itemId );
		if ( available )
		{
			if ( count <= 0 )
				player.HostAddItem( Item.Create( itemId, 1, canDrop: false, isJobItem: false, canSave: true ) );
			return;
		}

		if ( count > 0 )
			player.Inventory.RemoveItem( itemId, count );
	}

	public static bool TryAdvance( Player player, string expectedTaskId, int delta = 1 )
	{
		if ( !Networking.IsHost || !player.IsValid() || delta <= 0 ) return false;

		var playerQuest = player.Components.Get<PlayerQuest>();
		var definition = QuestDatabase.FindQuestById( QuestId );
		if ( !playerQuest.IsValid() || definition is null ) return false;

		var activeQuest = playerQuest.GetActiveQuest( definition );
		if ( activeQuest is null ) return false;
		if ( !string.Equals( activeQuest.CurrentQuestTask?.Id, expectedTaskId, StringComparison.OrdinalIgnoreCase ) ) return false;

		return QuestManager.TryAdvanceCount( playerQuest, definition, delta );
	}

	internal static void Reward( PlayerQuest questPlayer, int money, string itemId )
	{
		if ( !Networking.IsHost || !questPlayer.IsValid() ) return;

		var player = questPlayer.GameObject.Components.Get<Player>( FindMode.EverythingInSelfAndParent );
		if ( !player.IsValid() ) return;

		if ( money > 0 )
			player.Money = (int)Math.Clamp( (long)player.Money + money, 0L, int.MaxValue );

		if ( !string.IsNullOrWhiteSpace( itemId ) && player.Inventory is not null && player.Inventory.GetTotalCount( itemId ) <= 0 )
			player.HostAddItem( Item.Create( itemId, 1, canDrop: false, isJobItem: false, canSave: true ) );

	}
}

public sealed class MarkIntroQuestHandler : IQuestHandler
{
	public string Id => MarkIntroQuest.QuestId;

	public void OnQuestCompleted( PlayerQuest player, Quest quest )
	{
		Log.Info( $"[Mark] Tutorial completed by {player?.GameObject?.Network.Owner?.DisplayName ?? "unknown"}." );
	}
}

public sealed class MarkIntroHitTaskHandler : IQuestHandler
{
	public string Id => MarkIntroQuest.HitTaskId;

	public void OnTaskCompleted( PlayerQuest player, Quest quest, QuestTaskDefinition task )
	{
		MarkIntroQuest.Reward( player, 50, null );
	}
}

public sealed class MarkIntroBuyDoorsTaskHandler : IQuestHandler
{
	public string Id => MarkIntroQuest.BuyDoorsTaskId;

	public void OnTaskCompleted( PlayerQuest player, Quest quest, QuestTaskDefinition task )
	{
		MarkIntroQuest.Reward( player, 50, null );
	}
}

public sealed class MarkIntroSpawnPropTaskHandler : IQuestHandler
{
	public string Id => MarkIntroQuest.SpawnPropTaskId;

	public void OnTaskCompleted( PlayerQuest player, Quest quest, QuestTaskDefinition task )
	{
		MarkIntroQuest.Reward( player, 0, null );
	}
}

public sealed class MarkIntroPhysgunPropTaskHandler : IQuestHandler
{
	public string Id => MarkIntroQuest.PhysgunPropTaskId;

	public void OnTaskCompleted( PlayerQuest player, Quest quest, QuestTaskDefinition task )
	{
		MarkIntroQuest.Reward( player, 0, null );
	}
}

public sealed class MarkIntroReturnTaskHandler : IQuestHandler
{
	public string Id => MarkIntroQuest.ReturnTaskId;

	public void OnTaskCompleted( PlayerQuest player, Quest quest, QuestTaskDefinition task )
	{
		MarkIntroQuest.Reward( player, 100, "burger" );
	}
}
