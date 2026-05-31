/// <summary>
/// Per-job handler. Implement this and register via <see cref="JobHandlerRegistry"/>
/// to receive callbacks when players interact with a specific job.
/// </summary>
public interface IJobHandler
{
	/// <summary>
	/// The job ID this handler is responsible for (must match <see cref="JobDefinition.Id"/>).
	/// </summary>
	string JobId { get; }

	/// <summary>Called after a player spawns while holding this job (requires <see cref="JobDefinition.HasPostSpawned"/>).</summary>
	void PostSpawned( PlayerJob job, Player player );

	/// <summary>Called after a player loses (is demoted from) this job (requires <see cref="JobDefinition.HasPostDemote"/>).</summary>
	void PostDemote( PlayerJob job, Player player );

	/// <summary>Called after a player joins (switches to) this job (requires <see cref="JobDefinition.HasPostJoined"/>).</summary>
	void PostJoined( PlayerJob job, Player player );
}
