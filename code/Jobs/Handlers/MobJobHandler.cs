/// <summary>
/// Handler for the "mob" job.
/// Discovered automatically by <see cref="JobHandlerRegistry"/> via TypeLibrary.
/// </summary>
public sealed class MobJobHandler : IJobHandler
{
	public string JobId => "mob";

	public void PostSpawned( PlayerJob job, Player player )
	{
	}

	public void PostDemote( PlayerJob job, Player player )
	{
		Log.Info( $"[MobJob] {player.Network.Owner?.DisplayName} lost Mobster job" );
	}

	public void PostJoined( PlayerJob job, Player player )
	{
		Log.Info( $"[MobJob] {player.Network.Owner?.DisplayName} joined as Mobster" );
		player.HostGiveJobItem( "picklock", 1, canDrop: false, canSave: false );
	}
}
