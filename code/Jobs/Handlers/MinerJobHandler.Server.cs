/// <summary>
/// Handler for the "miner" job.
/// Discovered automatically by <see cref="JobHandlerRegistry"/> via TypeLibrary.
/// </summary>
public sealed class MinerJobHandler : IJobHandler
{
	public string JobId => "miner";

	public void PostSpawned( PlayerJob job, Player player )
	{
	}

	public void PostDemote( PlayerJob job, Player player )
	{
	}

	public void PostJoined( PlayerJob job, Player player )
	{
		player.HostGiveJobItem( "pickaxe", 1, canDrop: false, canSave: false );
	}
}
