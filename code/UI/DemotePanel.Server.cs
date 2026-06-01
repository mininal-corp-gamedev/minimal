using Sandbox;

namespace Sandbox;

public sealed partial class DemotePanel
{
	private static void RpcRequestDemoteServer( long targetSteamId )
	{
		if ( !Networking.IsHost )
			return;

		var caller = Rpc.Caller;
		if ( caller is null )
			return;

		if ( caller.SteamId.Value == targetSteamId )
		{
			NotifyDemoteCaller( caller, GameLocalization.Phrase( "notify.demote.self", "You cannot demote yourself." ), false );
			return;
		}

		var target = Player.FindPlayerBySteamId( targetSteamId );
		if ( !target.IsValid() )
		{
			NotifyDemoteCaller( caller, GameLocalization.Phrase( "notify.player.not_found", "Player not found." ), false );
			return;
		}

		if ( !VoteManager.CanDemoteTarget( target, out var reason ) )
		{
			NotifyDemoteCaller( caller, reason, false );
			return;
		}

		if ( VoteManager.TryStartDemoteVote( target ) )
			NotifyDemoteCaller( caller, GameLocalization.Phrase( "notify.demote.started", "Demote vote started." ), true );
		else
			NotifyDemoteCaller( caller, GameLocalization.Phrase( "notify.vote.could_not_start", "Could not start vote." ), false );
	}

	private static void NotifyDemoteCaller( Connection caller, string text, bool success )
	{
		using ( Rpc.FilterInclude( x => x.SteamId.Value == caller.SteamId.Value ) )
		{
			RpcReceiveDemoteResult( text, success );
		}
	}
}
