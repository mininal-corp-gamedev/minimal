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
	}

	/// <summary>
	/// Выдать игроку квест. Проверяет правила: не дублировать активный, не выдавать законченный,
	/// учитывает CanGetSameCategoryQuest нового квеста.
	/// </summary>
	public bool GiveQuest( PlayerQuest player, QuestDefinition def )
	{
		if ( !Networking.IsHost ) return false;
		if ( !player.IsValid() || def == null ) return false;

		if ( def.QuestTasks == null || def.QuestTasks.Count == 0 )
		{
			Logger.Warning( $"GiveQuest: '{def.Id}' has no tasks" );
			return false;
		}

		if ( player.HasActiveQuest( def ) ) return false;

		if ( player.HasFinishedQuest( def ) )
		{
			if ( !def.CanRepeat ) return false;

			// Перепрохождение разрешено — убираем старую запись о завершении.
			player.FinishedQuests.RemoveAll( q => q.QuestDefinition == def );
		}

		if ( !def.CanGetSameCategoryQuest && player.CurrentQuests.Any( q => q.QuestDefinition != null && q.QuestDefinition.Category == def.Category ) )
			return false;

		var quest = new Quest
		{
			QuestDefinition = def,
			CurrentQuestTask = def.QuestTasks[0],
			CurrentCount = 0,
			IsFinish = false,
		};
		player.CurrentQuests.Add( quest );
		player.OnHostStateChanged();
		return true;
	}

	/// <summary>Отменить активный квест: удаляется из CurrentQuests без записи в FinishedQuests.</summary>
	public bool CancelQuest( PlayerQuest player, QuestDefinition def )
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
	{
		if ( !Networking.IsHost ) return false;
		if ( !player.IsValid() || def == null ) return false;

		var quest = player.GetActiveQuest( def );
		if ( quest == null || quest.IsFinish ) return false;
		return CompleteTaskInternal( player, quest );
	}

	/// <summary>Перейти к произвольной задаче квеста (в пределах списка). Count сбрасывается.</summary>
	public bool SetTaskIndex( PlayerQuest player, QuestDefinition def, int taskIndex )
	{
		if ( !Networking.IsHost ) return false;
		if ( !player.IsValid() || def == null ) return false;

		var quest = player.GetActiveQuest( def );
		if ( quest == null || quest.IsFinish ) return false;
		if ( def.QuestTasks == null || taskIndex < 0 || taskIndex >= def.QuestTasks.Count ) return false;

		quest.CurrentQuestTask = def.QuestTasks[taskIndex];
		quest.CurrentCount = 0;
		player.OnHostStateChanged();
		return true;
	}

	private bool CompleteTaskInternal( PlayerQuest player, Quest quest )
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
		player.OnHostStateChanged();
		return true;
	}

	private void FinishQuestInternal( PlayerQuest player, Quest quest )
	{
		quest.IsFinish = true;
		player.CurrentQuests.Remove( quest );
		player.FinishedQuests.Add( quest );

		var def = quest.QuestDefinition;
		if ( def != null && def.HasPostCompleted )
			QuestDatabase.Get( def.Id )?.OnQuestCompleted( player, quest );

		player.OnHostStateChanged();
	}
}
