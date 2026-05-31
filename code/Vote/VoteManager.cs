using Sandbox;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

public sealed class VoteManager : Component
{
	public static VoteManager Instance { get; private set; }

	[Property] public int MaxVote { get; set; } = 1;
	[Property] public float TimeVote { get; set; } = 10f;

	private ActiveVote _activeVote;
	private int _nextVoteId = 1;

	protected override void OnAwake()
	{
		if ( Instance == null )
			Instance = this;
	}

	protected override void OnDestroy()
	{
		if ( Instance == this )
			Instance = null;
	}

	public static bool HasActiveVote => Instance?._activeVote is not null;

	public static bool TryStartJobVote( Player requester, JobDefinition jobDefinition )
	{
#if SERVER
		if ( !Networking.IsHost )
			return false;

		if ( !requester.IsValid() || !requester.Job.IsValid() || jobDefinition is null )
			return false;

		return EnsureInstance()?.StartVote(
			GameLocalization.Format( "vote.job.header", "{0} wants to become {1}", GetPlayerName( requester ), GameLocalization.JobHeader( jobDefinition ) ),
			() =>
			{
				if ( !requester.IsValid() || !requester.Job.IsValid() )
					return;

				if ( !CanTakeJob( requester, jobDefinition, out _ ) )
					return;

				requester.Job.SetJob( jobDefinition.Id );
				Notify( GameLocalization.Format( "notify.vote.became_job", "{0} became {1}.", GetPlayerName( requester ), GameLocalization.JobHeader( jobDefinition ) ), true );
			} ) ?? false;
#else
		return false;
#endif
	}

	public static bool TryStartDemoteVote( Player target )
	{
#if SERVER
		if ( !Networking.IsHost )
			return false;

		if ( !target.IsValid() || !target.Job.IsValid() || !CanDemoteTarget( target, out _ ) )
			return false;

		var oldJobName = GameLocalization.JobHeader( target.Job.JobDefinition );
		return EnsureInstance()?.StartVote(
			GameLocalization.Format( "vote.demote.header", "Demote {0} from {1}?", GetPlayerName( target ), oldJobName ),
			() =>
			{
				if ( !target.IsValid() || !target.Job.IsValid() )
					return;

				if ( !CanDemoteTarget( target, out _ ) )
					return;

				var demoteJob = JobManager.Instance?.DemoteJob ?? JobDatabase.Get( PlayerJob.DefaultJobId );
				if ( demoteJob is null )
					return;

				target.Job.SetJob( demoteJob.Id );
				Notify( GameLocalization.Format( "notify.vote.demoted_to", "{0} was demoted to {1}.", GetPlayerName( target ), GameLocalization.JobHeader( demoteJob ) ), true );
			} ) ?? false;
#else
		return false;
#endif
	}

	public static bool CanTakeJob( Player player, JobDefinition jobDefinition, out string reason )
	{
		reason = "";

		if ( !player.IsValid() || !player.Job.IsValid() )
		{
			reason = GameLocalization.Phrase( "notify.player.not_ready", "Your player is not ready." );
			return false;
		}

		if ( player.IsArrested )
		{
			reason = GameLocalization.Phrase( "notify.player.arrested", "You are arrested." );
			return false;
		}

		if ( jobDefinition is null )
		{
			reason = GameLocalization.Phrase( "notify.jobs.not_found", "Job not found." );
			return false;
		}

		if ( string.Equals( player.Job.JobId, jobDefinition.Id, StringComparison.Ordinal ) )
		{
			reason = GameLocalization.Phrase( "notify.jobs.already_have", "You already have this job." );
			return false;
		}

		if ( jobDefinition.FromJobs is { Count: > 0 } )
		{
			var currentJobId = player.Job.JobId;
			var allowedFromCurrentJob = jobDefinition.FromJobs.Any( allowedJob =>
				allowedJob is not null && string.Equals( allowedJob.Id, currentJobId, StringComparison.Ordinal ) );

			if ( !allowedFromCurrentJob )
			{
				reason = GameLocalization.Phrase( "notify.jobs.not_allowed_from_job", "This job is not available to you." );
				return false;
			}
		}

		if ( jobDefinition.MaxCount > 0 && GetJobPlayerCount( jobDefinition.Id ) >= jobDefinition.MaxCount )
		{
			reason = GameLocalization.Format( "notify.jobs.no_free_slots", "No free slots for {0}.", GameLocalization.JobHeader( jobDefinition ) );
			return false;
		}

		return true;
	}

	public static bool CanDemoteTarget( Player target, out string reason )
	{
		reason = "";

		if ( !target.IsValid() || !target.Job.IsValid() )
		{
			reason = GameLocalization.Phrase( "notify.player.not_ready_other", "Player is not ready." );
			return false;
		}

		if ( target.GameObject.Network.Owner is null || target.GameObject.Network.Owner.SteamId.Value <= 0 )
		{
			reason = GameLocalization.Phrase( "notify.player.connection_not_ready", "Player connection is not ready." );
			return false;
		}

		var job = target.Job.JobDefinition;
		if ( job is null )
		{
			reason = GameLocalization.Phrase( "notify.player.no_job", "Player has no job." );
			return false;
		}

		if ( string.Equals( job.Id, PlayerJob.DefaultJobId, StringComparison.OrdinalIgnoreCase ) )
		{
			reason = GameLocalization.Phrase( "notify.demote.citizen", "Citizen cannot be demoted." );
			return false;
		}

		if ( !job.CanDemote )
		{
			reason = GameLocalization.Phrase( "notify.demote.job_denied", "This job cannot be demoted." );
			return false;
		}

		return true;
	}

	[Rpc.Host]
	public static void RpcSubmitVote( int voteId, bool yes )
	{
#if SERVER
		if ( !Networking.IsHost )
			return;

		Instance?.ReceiveVote( Rpc.Caller, voteId, yes );
#endif
	}

#if SERVER
	private bool StartVote( string header, Action onPassed )
	{
		if ( !Networking.IsHost )
			return false;

		if ( _activeVote is not null )
			return false;

		var voters = Connection.All
			.Where( x => x is not null && x.SteamId.Value > 0 )
			.Select( x => x.SteamId.Value )
			.ToHashSet();

		if ( voters.Count <= 0 )
			return false;

		_activeVote = new ActiveVote
		{
			Id = _nextVoteId++,
			Header = header,
			EligibleVoters = voters,
			OnPassed = onPassed,
		};

		RpcOpenVote( _activeVote.Id, header );
		_ = FinishVoteAfterDelay( _activeVote.Id );
		return true;
	}

	private void ReceiveVote( Connection caller, int voteId, bool yes )
	{
		if ( caller is null || _activeVote is null || _activeVote.Id != voteId )
			return;

		var steamId = caller.SteamId.Value;
		if ( !_activeVote.EligibleVoters.Contains( steamId ) || _activeVote.Answers.ContainsKey( steamId ) )
			return;

		_activeVote.Answers[steamId] = yes;

		using ( Rpc.FilterInclude( x => x.SteamId.Value == steamId ) )
		{
			RpcMarkVoteAnswered( voteId );
		}

		if ( _activeVote.Answers.Count >= _activeVote.EligibleVoters.Count )
			FinishVote( voteId );
	}

	private async Task FinishVoteAfterDelay( int voteId )
	{
		var seconds = MathF.Max( 1f, TimeVote );
		await Task.DelaySeconds( seconds );
		FinishVote( voteId );
	}

	private void FinishVote( int voteId )
	{
		if ( !Networking.IsHost || _activeVote is null || _activeVote.Id != voteId )
			return;

		var vote = _activeVote;
		_activeVote = null;

		var yes = vote.Answers.Values.Count( x => x );
		var no = vote.Answers.Values.Count( x => !x );
		var passed = yes > 0 && yes >= no;

		RpcCloseVote( vote.Id );
		Notify( passed
			? GameLocalization.Format( "notify.vote.passed", "Vote passed: {0}", vote.Header )
			: GameLocalization.Format( "notify.vote.failed", "Vote failed: {0}", vote.Header ), passed );

		if ( passed )
			vote.OnPassed?.Invoke();
	}

	private static VoteManager EnsureInstance()
	{
		if ( Instance.IsValid() )
			return Instance;

		var scene = Game.ActiveScene;
		if ( scene is null )
			return null;

		var obj = scene.CreateObject();
		obj.Name = "VoteManager";
		return obj.Components.Create<VoteManager>();
	}
#endif

	private static int GetJobPlayerCount( string jobId )
	{
		var scene = Game.ActiveScene;
		if ( scene is null || string.IsNullOrWhiteSpace( jobId ) )
			return 0;

		var count = 0;
		foreach ( var player in scene.GetAllComponents<Player>() )
		{
			if ( string.Equals( player?.Job?.JobId, jobId, StringComparison.Ordinal ) )
				count++;
		}

		return count;
	}

#if SERVER
	private static string GetPlayerName( Player player )
	{
		return player?.GameObject?.Network.Owner?.DisplayName ?? GameLocalization.Phrase( "common.player", "Player" );
	}

	private static void Notify( string message, bool success )
	{
		RpcReceiveVoteNotice( message, success );
	}
#endif

	[Rpc.Broadcast]
	private static void RpcOpenVote( int voteId, string header )
	{
		VotePanel.Open( voteId, header );
	}

	[Rpc.Broadcast]
	private static void RpcMarkVoteAnswered( int voteId )
	{
		VotePanel.MarkAnswered( voteId );
	}

	[Rpc.Broadcast]
	private static void RpcCloseVote( int voteId )
	{
		VotePanel.Close( voteId );
	}

	[Rpc.Broadcast]
	private static void RpcReceiveVoteNotice( string message, bool success )
	{
		if ( success )
			Notification.Info( message, 3.5f );
		else
			Notification.Error( message, 3.5f );
	}

	private sealed class ActiveVote
	{
		public int Id { get; set; }
		public string Header { get; set; }
		public HashSet<long> EligibleVoters { get; set; } = new();
		public Dictionary<long, bool> Answers { get; } = new();
		public Action OnPassed { get; set; }
	}
}
