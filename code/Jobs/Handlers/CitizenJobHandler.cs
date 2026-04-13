/// <summary>
/// Handler for the "citizen" job.
/// Discovered automatically by <see cref="JobHandlerRegistry"/> via TypeLibrary.
/// </summary>
public sealed class CitizenJobHandler : IJobHandler
{
	public string JobId => "citizen";

	public void PostSpawned( PlayerJob job, Player player )
	{
		Log.Info( $"[CitizenJob] {player.Network.Owner?.DisplayName} spawned as Citizen" );
	}

	public void PostDemote( PlayerJob job, Player player )
	{
		Log.Info( $"[CitizenJob] {player.Network.Owner?.DisplayName} lost Citizen job" );
	}

	public void PostJoined( PlayerJob job, Player player )
	{
		Log.Info( $"[CitizenJob] {player.Network.Owner?.DisplayName} joined as Citizen" );
	}
}
