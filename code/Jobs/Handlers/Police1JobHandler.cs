/// <summary>
/// Handler for the "police1" job.
/// Discovered automatically by <see cref="JobHandlerRegistry"/> via TypeLibrary.
/// </summary>
public sealed class Police1JobHandler : IJobHandler
{
	private const string WorkshopSkinPackageId = "raf/sheriffshirt";
    private const string WorkshopSkinPackageId2 = "raf/sheriffpants";

    public string JobId => "police1";

	public void PostSpawned( PlayerJob job, Player player )
	{
		player.HostGiveFullArmor();
		player.HostAddJobWorkshopItem( WorkshopSkinPackageId );
        player.HostAddJobWorkshopItem(WorkshopSkinPackageId2);
    }

	public void PostDemote( PlayerJob job, Player player )
	{
		Log.Info( $"[Police1Job] {player.Network.Owner?.DisplayName} lost Officer job" );
		player.HostSetArmor( 0f );
		player.HostRemoveJobWorkshopItem( WorkshopSkinPackageId );
        player.HostRemoveJobWorkshopItem(WorkshopSkinPackageId2);
    }

	public void PostJoined( PlayerJob job, Player player )
	{
		Log.Info( $"[Police1Job] {player.Network.Owner?.DisplayName} joined as Officer" );
		player.HostGiveFullArmor();
		player.HostGiveJobItem( "handcuff", 1, canDrop: false, canSave: false );
		player.HostAddJobWorkshopItem( WorkshopSkinPackageId );
        player.HostAddJobWorkshopItem(WorkshopSkinPackageId2);
    }
}
