/// <summary>
/// Handler for the "police1" job.
/// Discovered automatically by <see cref="JobHandlerRegistry"/> via TypeLibrary.
/// </summary>
public sealed class Police1JobHandler : IJobHandler
{
	public string JobId => "police1";

	public void PostSpawned( PlayerJob job, Player player )
	{
		player.HostGiveFullArmor();
	}

	public void PostDemote( PlayerJob job, Player player )
	{
        player.HostSetArmor(0f);
    }

	public void PostJoined( PlayerJob job, Player player )
	{
		Log.Info( $"[Police1Job] {player.Network.Owner?.DisplayName} joined as Officer" );
		player.HostGiveFullArmor();
		player.HostGiveJobItem( "handcuff", 1, canDrop: false, canSave: false );
	}
}
