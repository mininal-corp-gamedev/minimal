public sealed partial class PlayerJob
{
	private void SetJobServer( string jobId )
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

	private void RemoveJobServer()
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

	private void NotifySpawnedServer()
	{
		var player = Player;
		if ( player.IsValid() )
			player.HostApplyJobWorkshopClothing( JobDefinition );

		JobHandlerRegistry.FirePostSpawned( this, player );
	}
}
