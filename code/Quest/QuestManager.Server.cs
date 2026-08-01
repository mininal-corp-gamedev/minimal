using Sandbox;
using Sandbox.Diagnostics;
using System.Linq;

public sealed partial class QuestManager
{
	public static Logger Logger = new( "QuestManager" );

	partial void OnStartHost()
	{
		if ( !Networking.IsHost ) return;

		QuestDatabase.Clear();
		QuestHandlerRegister.RegisterAll();

		var markQuest = QuestDatabase.FindQuestById( MarkIntroQuest.QuestId );
		Logger.Info( $"Server quest system ready. Mark quest found={markQuest != null}, tasks={markQuest?.QuestTasks?.Count ?? 0}." );
	}

	/// <summary>
	/// Выдать игроку квест. Проверяет правила: не дублировать активный, не выдавать законченный,
	/// учитывает CanGetSameCategoryQuest нового квеста.
	/// </summary>
	public bool GiveQuest( PlayerQuest player, QuestDefinition def )
		=> TryGiveQuest( player, def );

	public static bool TryGiveQuest( PlayerQuest player, QuestDefinition def )
	{
		if ( !Networking.IsHost )
		{
			Logger.Warning( "GiveQuest rejected: current peer is not the host" );
			return false;
		}

		if ( !player.IsValid() || def == null )
		{
			Logger.Warning( $"GiveQuest rejected: player valid={player.IsValid()}, definition valid={def != null}" );
			return false;
		}

		if ( def.QuestTasks == null || def.QuestTasks.Count == 0 )
		{
			Logger.Warning( $"GiveQuest: '{def.Id}' has no tasks" );
			return false;
		}

		if ( player.HasActiveQuest( def ) )
		{
			Logger.Warning( $"GiveQuest rejected: '{def.Id}' is already active" );
			return false;
		}

		if ( player.HasFinishedQuest( def ) )
		{
			if ( !def.CanRepeat )
			{
				Logger.Warning( $"GiveQuest rejected: '{def.Id}' is already finished and cannot repeat" );
				return false;
			}

			// Перепрохождение разрешено — убираем старую запись о завершении.
			player.FinishedQuests.RemoveAll( q => q.QuestDefinition == def );
		}

		if ( !def.CanGetSameCategoryQuest && player.CurrentQuests.Any( q => q.QuestDefinition != null && q.QuestDefinition.Category == def.Category ) )
		{
			Logger.Warning( $"GiveQuest rejected: category '{def.Category}' already has an active quest" );
			return false;
		}

		// Registration is idempotent and keeps server-side rewards working even when
		// the scene singleton was recreated by a hot reload.
		QuestHandlerRegister.RegisterAll();

		var quest = new Quest
		{
			QuestDefinition = def,
			CurrentQuestTask = def.QuestTasks[0],
			CurrentCount = 0,
			IsFinish = false,
		};
		player.CurrentQuests.Add( quest );
		MarkIntroQuest.ApplyInventoryUnlocks( player );
		player.OnHostStateChanged();
		return true;
	}

	/// <summary>Отменить активный квест: удаляется из CurrentQuests без записи в FinishedQuests.</summary>
	public bool CancelQuest( PlayerQuest player, QuestDefinition def )
		=> TryCancelQuest( player, def );

	public static bool TryCancelQuest( PlayerQuest player, QuestDefinition def )
	{
		if ( !Networking.IsHost ) return false;
		if ( !player.IsValid() || def == null ) return false;

		var quest = player.GetActiveQuest( def );
		if ( quest == null ) return false;

		player.CurrentQuests.Remove( quest );

		if ( def.HasPostCanceled )
			QuestDatabase.Get( def.Id )?.OnQuestCanceled( player, quest );

		player.OnHostStateChanged();
		return true;
	}

	/// <summary>Увеличить Count текущей задачи. При достижении Count задача завершается автоматически.</summary>
	public bool AdvanceCount( PlayerQuest player, QuestDefinition def, int delta = 1 )
		=> TryAdvanceCount( player, def, delta );

	public static bool TryAdvanceCount( PlayerQuest player, QuestDefinition def, int delta = 1 )
	{
		if ( !Networking.IsHost ) return false;
		if ( !player.IsValid() || def == null || delta <= 0 ) return false;

		var quest = player.GetActiveQuest( def );
		if ( quest == null || quest.IsFinish ) return false;

		var task = quest.CurrentQuestTask;
		if ( task == null ) return false;

		quest.CurrentCount = System.Math.Min( task.Count, quest.CurrentCount + delta );

		if ( quest.CurrentCount >= task.Count )
			return CompleteTaskInternal( player, quest );

		player.OnHostStateChanged();
		return true;
	}

	/// <summary>Принудительно завершить текущую задачу квеста, перейти к следующей или к завершению квеста.</summary>
	public bool CompleteTask( PlayerQuest player, QuestDefinition def )
		=> TryCompleteTask( player, def );

	public static bool TryCompleteTask( PlayerQuest player, QuestDefinition def )
	{
		if ( !Networking.IsHost ) return false;
		if ( !player.IsValid() || def == null ) return false;

		var quest = player.GetActiveQuest( def );
		if ( quest == null || quest.IsFinish ) return false;
		return CompleteTaskInternal( player, quest );
	}

	/// <summary>Перейти к произвольной задаче квеста (в пределах списка). Count сбрасывается.</summary>
	public bool SetTaskIndex( PlayerQuest player, QuestDefinition def, int taskIndex )
		=> TrySetTaskIndex( player, def, taskIndex );

	public static bool TrySetTaskIndex( PlayerQuest player, QuestDefinition def, int taskIndex )
	{
		if ( !Networking.IsHost ) return false;
		if ( !player.IsValid() || def == null ) return false;

		var quest = player.GetActiveQuest( def );
		if ( quest == null || quest.IsFinish ) return false;
		if ( def.QuestTasks == null || taskIndex < 0 || taskIndex >= def.QuestTasks.Count ) return false;

		quest.CurrentQuestTask = def.QuestTasks[taskIndex];
		quest.CurrentCount = 0;
		MarkIntroQuest.ApplyInventoryUnlocks( player );
		player.OnHostStateChanged();
		return true;
	}

	private static bool CompleteTaskInternal( PlayerQuest player, Quest quest )
	{
		var def = quest.QuestDefinition;
		var task = quest.CurrentQuestTask;
		if ( def == null || task == null ) return false;

		if ( task.HasPostCompleted )
			QuestDatabase.Get( task.Id )?.OnTaskCompleted( player, quest, task );

		var idx = def.QuestTasks.IndexOf( task );
		if ( idx < 0 || idx >= def.QuestTasks.Count - 1 )
		{
			FinishQuestInternal( player, quest );
			return true;
		}

		quest.CurrentQuestTask = def.QuestTasks[idx + 1];
		quest.CurrentCount = 0;
		MarkIntroQuest.ApplyInventoryUnlocks( player );
		player.OnHostStateChanged();
		return true;
	}

	private static void FinishQuestInternal( PlayerQuest player, Quest quest )
	{
		quest.IsFinish = true;
		player.CurrentQuests.Remove( quest );
		player.FinishedQuests.Add( quest );

		var def = quest.QuestDefinition;
		if ( def != null && def.HasPostCompleted )
			QuestDatabase.Get( def.Id )?.OnQuestCompleted( player, quest );

		MarkIntroQuest.ApplyInventoryUnlocks( player );
		player.OnHostStateChanged();
	}
}
