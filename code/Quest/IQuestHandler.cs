public interface IQuestHandler
{
	string Id { get; }

	void OnQuestCompleted( PlayerQuest player, Quest quest ) { }
	void OnQuestCanceled( PlayerQuest player, Quest quest ) { }
	void OnTaskCompleted( PlayerQuest player, Quest quest, QuestTaskDefinition task ) { }
}