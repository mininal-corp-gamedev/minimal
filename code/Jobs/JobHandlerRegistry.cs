using System;

public static class JobHandlerRegistry
{
	private static Dictionary<string, IJobHandler> _handlers;

	private static void EnsureLoaded()
	{
		if ( _handlers != null )
			return;

		_handlers = new();

		foreach ( var type in TypeLibrary.GetTypes<IJobHandler>() )
		{
			if ( type.IsAbstract || type.IsInterface )
				continue;

			if ( TypeLibrary.Create<IJobHandler>( type.TargetType ) is not { } handler )
				continue;

			if ( _handlers.TryGetValue( handler.JobId, out var existing ) )
			{
				Log.Warning( $"JobHandlerRegistry: duplicate handler for '{handler.JobId}' ({existing.GetType().Name} vs {handler.GetType().Name})" );
				continue;
			}

			_handlers[handler.JobId] = handler;
		}
	}

	public static IJobHandler Get( string jobId )
	{
		EnsureLoaded();
		_handlers.TryGetValue( jobId, out var handler );
		return handler;
	}

	internal static void FirePostSpawned( PlayerJob job, Player player )
	{
		var def = job.JobDefinition;
        if ( def == null || !def.HasPostSpawned ) return;

        Get( def.Id )?.PostSpawned( job, player );
	}

	internal static void FirePostDemote( PlayerJob job, Player player, JobDefinition oldDef )
	{
		if ( oldDef == null || !oldDef.HasPostDemote ) return;

		Get( oldDef.Id )?.PostDemote( job, player );
	}

	internal static void FirePostJoined( PlayerJob job, Player player )
	{
		var def = job.JobDefinition;
		if ( def == null || !def.HasPostJoined ) return;

		Get( def.Id )?.PostJoined( job, player );
	}
}
