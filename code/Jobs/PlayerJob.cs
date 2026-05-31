using System;

public sealed partial class PlayerJob : Component
{
	public const string DefaultJobId = "citizen";

	public static Action<Player, JobDefinition> OnJobJoined;
	public static Action<Player, JobDefinition> OnJobChanged;
	public static Action<Player> OnJobDemote;

	[Sync( SyncFlags.FromHost )]
	public string JobId { get; set; } = "";

	public JobDefinition JobDefinition =>
		string.IsNullOrEmpty( JobId ) ? null : JobDatabase.Get( JobId );

	private Player Player => Components.Get<Player>( FindMode.EverythingInSelfAndAncestors );

	private string _previousJobId = "";

	/// <summary>
	/// Called by Player on NetworkInit to assign the default job from host.
	/// </summary>
	public void AssignDefault()
	{
		SetJob( DefaultJobId );
	}

	[Rpc.Host]
	public void SetJob( string jobId )
	{
#if SERVER
		SetJobServer( jobId );
#endif
	}

	[Rpc.Host]
	public void RemoveJob()
	{
#if SERVER
		RemoveJobServer();
#endif
	}

	/// <summary>
	/// Called by Player after each spawn to fire the PostSpawned handler.
	/// </summary>
	public void NotifySpawned()
	{
#if SERVER
		NotifySpawnedServer();
#endif
	}

	protected override void OnUpdate()
	{
		if ( JobId != _previousJobId )
			_previousJobId = JobId;
	}
}
