using Sandbox;

public sealed partial class QuestPress
{
	partial void TryAdvanceForQuestServer( PlayerQuest ply )
	{
		if ( !Networking.IsHost ) return;
		if ( !ply.IsValid() ) return;
		if ( Rpc.Caller != null && !Rpc.Caller.IsHost && ply.Network.Owner != Rpc.Caller ) return;
		if ( Quest == null || QuestTask == null ) return;

		var active = ply.GetActiveQuest( Quest );
		if ( active == null || active.CurrentQuestTask != QuestTask ) return;

		QuestManager.TryAdvanceCount( ply, Quest );
	}
}
