public static class QuestHandlerRegister
{
	public static void RegisterAll()
	{
		QuestDatabase.Register( new QuestFirstHandler() );
	}
}