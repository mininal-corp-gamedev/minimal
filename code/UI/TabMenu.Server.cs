using Sandbox;
using System;

namespace Sandbox;

public sealed partial class TabMenu
{
	private void RpcRequestJoinJobServer( string jobId )
	{
		if ( !Networking.IsHost )
			return;

		var caller = Rpc.Caller;
		if ( caller is null )
			return;

		var normalized = (jobId ?? string.Empty).Trim();
		if ( string.IsNullOrEmpty( normalized ) )
		{
			NotifyJoinJobCaller( caller, GameLocalization.Phrase( "notify.jobs.empty_id", "Job id is empty." ), false );
			return;
		}

		var jobDefinition = JobDatabase.Get( normalized );
		if ( jobDefinition is null )
		{
			NotifyJoinJobCaller( caller, GameLocalization.Phrase( "notify.jobs.not_found", "Job not found." ), false );
			return;
		}

		var callerPlayer = Player.FindPlayerBySteamId( caller.SteamId.Value );
		if ( !callerPlayer.IsValid() || !callerPlayer.Job.IsValid() )
		{
			NotifyJoinJobCaller( caller, GameLocalization.Phrase( "notify.player.not_ready", "Your player is not ready." ), false );
			return;
		}

		if ( callerPlayer.IsArrested )
		{
			NotifyJoinJobCaller( caller, GameLocalization.Phrase( "notify.player.arrested", "You are arrested." ), false );
			return;
		}

		if ( string.Equals( callerPlayer.Job.JobId, normalized, StringComparison.Ordinal ) )
		{
			NotifyJoinJobCaller( caller, GameLocalization.Phrase( "notify.jobs.already_have", "You already have this job." ), true );
			return;
		}

		if ( !VoteManager.CanTakeJob( callerPlayer, jobDefinition, out var reason ) )
		{
			NotifyJoinJobCaller( caller, reason, false );
			return;
		}

		if ( jobDefinition.Vote && Connection.All.Count > 2 )
		{
			if ( VoteManager.TryStartJobVote( callerPlayer, jobDefinition ) )
				NotifyJoinJobCaller( caller, GameLocalization.Format( "notify.vote.started_for", "Vote started for {0}.", GameLocalization.JobHeader( jobDefinition ) ), true );
			else
				NotifyJoinJobCaller( caller, GameLocalization.Phrase( "notify.vote.could_not_start", "Could not start vote." ), false );

			return;
		}

		callerPlayer.Job.HostSetJob( normalized );
		NotifyJoinJobCaller( caller, GameLocalization.Format( "notify.jobs.joined", "You joined: {0}.", GameLocalization.JobHeader( jobDefinition ) ), true );
	}

	private static void NotifyJoinJobCaller( Connection caller, string text, bool success )
	{
		using ( Rpc.FilterInclude( x => x.SteamId.Value == caller.SteamId.Value ) )
		{
			RpcReceiveJoinJobResult( text, success );
		}
	}
}
