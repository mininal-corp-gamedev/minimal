/// <summary>
/// Handler for the "sheriff" job.
/// Discovered automatically by <see cref="JobHandlerRegistry"/> via TypeLibrary.
/// </summary>
public sealed class SheriffJobHandler : IJobHandler
{
	public string JobId => "sheriff";

	public void PostSpawned( PlayerJob job, Player player )
	{
		player.HostGiveFullArmor();
	}

	public void PostDemote( PlayerJob job, Player player )
	{
		Log.Info( $"[SheriffJob] {player.Network.Owner?.DisplayName} lost Sheriff job" );
	}

	public void PostJoined( PlayerJob job, Player player )
	{
		Log.Info( $"[SheriffJob] {player.Network.Owner?.DisplayName} joined as Sheriff" );
		player.HostGiveFullArmor();
		player.HostGiveJobItem( "handcuff", 1, canDrop: false, canSave: false );
	}
}
