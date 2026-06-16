using Sandbox;
using System.Collections.Generic;
using System.Linq;

public static class QuestDatabase
{
	private static readonly Dictionary<string, IQuestHandler> handlers = new();

	public static void Register( IQuestHandler handler )
	{
		if ( handler == null || string.IsNullOrEmpty( handler.Id ) )
			return;
		handlers[handler.Id] = handler;
	}

	public static void Clear() => handlers.Clear();

	public static IQuestHandler Get( string id )
	{
		if ( string.IsNullOrEmpty( id ) )
			return null;
		return handlers.TryGetValue( id, out var h ) ? h : null;
	}

	public static QuestDefinition FindQuestById( string id )
	{
		if ( string.IsNullOrEmpty( id ) )
			return null;
		return ResourceLibrary.GetAll<QuestDefinition>().FirstOrDefault( q => q != null && q.Id == id );
	}

	public static QuestTaskDefinition FindTaskById( string id )
	{
		if ( string.IsNullOrEmpty( id ) )
			return null;
		return ResourceLibrary.GetAll<QuestTaskDefinition>().FirstOrDefault( t => t != null && t.Id == id );
	}
}
