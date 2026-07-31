public static class QuestHandlerRegister
{
	public static void RegisterAll()
	{
		QuestDatabase.Register( new QuestFirstHandler() );
		QuestDatabase.Register( new MarkIntroQuestHandler() );
		QuestDatabase.Register( new MarkIntroHitTaskHandler() );
		QuestDatabase.Register( new MarkIntroBuyDoorsTaskHandler() );
		QuestDatabase.Register( new MarkIntroReturnTaskHandler() );
	}
}
