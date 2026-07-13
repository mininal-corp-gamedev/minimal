using System;
using System.Collections.Generic;
using System.Linq;

namespace Minimal.PhoneSystem;

public static class PhoneAppDatabase
{
	private static IReadOnlyList<PhoneAppDefinition> _apps;
	private static Dictionary<string, PhoneAppDefinition> _byId;

	public static IReadOnlyList<PhoneAppDefinition> GetAll()
	{
		EnsureLoaded();
		return _apps;
	}

	public static PhoneAppDefinition Get( string id )
	{
		EnsureLoaded();
		_byId.TryGetValue( Normalize( id ), out var app );
		return app;
	}

	public static void Reload()
	{
		_apps = null;
		_byId = null;
	}

	private static void EnsureLoaded()
	{
		if ( _apps is not null )
			return;

		var apps = new List<PhoneAppDefinition>();
		var byId = new Dictionary<string, PhoneAppDefinition>( StringComparer.OrdinalIgnoreCase );

		foreach ( var app in ResourceLibrary.GetAll<PhoneAppDefinition>().Where( app => app is not null ) )
		{
			var id = Normalize( app.Id );
			if ( string.IsNullOrWhiteSpace( id ) )
			{
				Log.Warning( $"PhoneAppDatabase: app '{app.Header}' has an empty resource ID." );
				continue;
			}

			if ( byId.TryGetValue( id, out var existing ) )
			{
				Log.Warning( $"PhoneAppDatabase: duplicate ID '{id}' ({existing.Header} / {app.Header})." );
				continue;
			}

			byId[id] = app;
			if ( app.Enabled )
				apps.Add( app );
		}

		_apps = apps
			.OrderBy( app => app.SortOrder )
			.ThenBy( app => app.Header ?? "", StringComparer.OrdinalIgnoreCase )
			.ToList();
		_byId = byId;
	}

	private static string Normalize( string value ) => (value ?? "").Trim();
}
