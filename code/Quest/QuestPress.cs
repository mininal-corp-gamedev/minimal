using Sandbox;

public sealed partial class QuestPress : Component, Component.IPressable
{
	[Property] public QuestDefinition Quest { get; set; }
	[Property] public QuestTaskDefinition QuestTask { get; set; }

	public bool Press( IPressable.Event e )
	{
		if ( Quest == null || QuestTask == null ) return false;
		if ( !e.Source.GameObject.Components.TryGet<PlayerQuest>( out var ply, FindMode.EverythingInSelfAndParent ) ) return false;

		RpcTryAdvanceForQuest( ply );
		return true;
	}

	[Rpc.Host]
	private void RpcTryAdvanceForQuest( PlayerQuest ply ) => TryAdvanceForQuestServer( ply );
	partial void TryAdvanceForQuestServer( PlayerQuest ply );
}
