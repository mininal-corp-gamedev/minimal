public static class QuestHandlerRegister
{
	public static void RegisterAll()
	{
		QuestDatabase.Register( new QuestFirstHandler() );
		QuestDatabase.Register( new MarkIntroQuestHandler() );
		QuestDatabase.Register( new MarkIntroHitTaskHandler() );
		QuestDatabase.Register( new MarkIntroBuyDoorsTaskHandler() );
		QuestDatabase.Register( new MarkIntroSpawnPropTaskHandler() );
		QuestDatabase.Register( new MarkIntroPhysgunPropTaskHandler() );
		QuestDatabase.Register( new MarkIntroReturnTaskHandler() );
	}
}
