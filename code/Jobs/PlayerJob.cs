using System;

public sealed class PlayerJob : Component
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
		var newDef = JobDatabase.Get( jobId );
		if ( newDef == null )
			return;

		var player = Player;
		var hadJob = !string.IsNullOrEmpty( JobId );
		var oldDef = JobDefinition;

		JobId = jobId;

		if ( hadJob )
		{
			ShopManager.RemoveShopObjectsOnJobChange( player );
			JobHandlerRegistry.FirePostDemote( this, player, oldDef );
			OnJobChanged?.Invoke( player, newDef );
		}
		else
		{
			OnJobJoined?.Invoke( player, newDef );
		}

		if ( player.IsValid() )
			player.HostApplyJobWorkshopClothing( newDef );

		if ( jobId != DefaultJobId && player.IsValid() )
			player.HostGrantAchievement( "change_job" );

		JobHandlerRegistry.FirePostJoined( this, player );
	}

	[Rpc.Host]
	public void RemoveJob()
	{
		if ( string.IsNullOrEmpty( JobId ) )
			return;

		var player = Player;
		var oldDef = JobDefinition;

		JobId = "";

		ShopManager.RemoveShopObjectsOnJobChange( player );
		JobHandlerRegistry.FirePostDemote( this, player, oldDef );
		OnJobDemote?.Invoke( player );

		if ( player.IsValid() )
			player.HostClearJobWorkshopClothing();
	}

	/// <summary>
	/// Called by Player after each spawn to fire the PostSpawned handler.
	/// </summary>
	public void NotifySpawned()
	{
		var player = Player;
		if ( player.IsValid() )
			player.HostApplyJobWorkshopClothing( JobDefinition );

		JobHandlerRegistry.FirePostSpawned( this, player );
	}

	protected override void OnUpdate()
	{
		if ( JobId != _previousJobId )
			_previousJobId = JobId;
	}
}
