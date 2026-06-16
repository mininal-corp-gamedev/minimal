using Sandbox;
using Sandbox.Diagnostics;

public sealed class QuestFirstHandler : IQuestHandler
{
	private static readonly Logger Logger = new( "QuestFirstHandler" );

	public string Id => "q1";

	public void OnQuestCompleted( PlayerQuest player, Quest quest )
	{
		Logger.Info( $"Quest q1 completed by {player?.GameObject?.Name}" );
	}

	public void OnQuestCanceled( PlayerQuest player, Quest quest )
	{
		Logger.Info( $"Quest q1 canceled by {player?.GameObject?.Name}" );
	}
}
