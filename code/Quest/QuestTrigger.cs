using Sandbox;

public sealed partial class QuestTrigger : Component, Component.ITriggerListener
{
	[Property] public QuestDefinition Quest { get; set; }
	[Property] public QuestTaskDefinition QuestTask { get; set; }

	void ITriggerListener.OnTriggerEnter( GameObject other )
	{
		if ( Quest == null || QuestTask == null ) return;
		if ( !other.Components.TryGet<PlayerQuest>( out var ply, FindMode.EverythingInSelfAndParent ) ) return;

		// Чтобы трigger не выстреливал у всех клиентов одновременно — отправляет только владелец игрока.
		if ( ply.Network.Owner != Connection.Local ) return;

		RpcTryAdvanceForQuest( ply );
	}

	[Rpc.Host]
	private void RpcTryAdvanceForQuest( PlayerQuest ply ) => TryAdvanceForQuestServer( ply );
	partial void TryAdvanceForQuestServer( PlayerQuest ply );
}
