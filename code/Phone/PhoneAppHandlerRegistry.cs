using System;
using System.Collections.Generic;

namespace Minimal.PhoneSystem;

public static class PhoneAppHandlerRegistry
{
	private static Dictionary<string, IPhoneAppHandler> _handlers;

	public static IPhoneAppHandler Get( string appId )
	{
		EnsureLoaded();
		_handlers.TryGetValue( Normalize( appId ), out var handler );
		return handler;
	}

	public static void FireOpened( PhoneAppDefinition definition )
	{
		if ( definition is null )
			return;

		Get( definition.Id )?.OnOpened( CreateContext( definition ) );
	}

	public static void FireAction( PhoneAppDefinition definition, string actionId )
	{
		if ( definition is null || string.IsNullOrWhiteSpace( actionId ) )
			return;

		var handler = Get( definition.Id );
		if ( handler is null )
		{
			Log.Warning( $"PhoneAppHandlerRegistry: no handler for app '{definition.Id}', action '{actionId}'." );
			return;
		}

		handler.OnAction( CreateContext( definition ), actionId.Trim() );
	}

	private static PhoneAppContext CreateContext( PhoneAppDefinition definition ) => new()
	{
		Definition = definition,
		Player = Player.Local,
		Connection = Connection.Local
	};

	private static void EnsureLoaded()
	{
		if ( _handlers is not null )
			return;

		_handlers = new Dictionary<string, IPhoneAppHandler>( StringComparer.OrdinalIgnoreCase );
		foreach ( var type in TypeLibrary.GetTypes<IPhoneAppHandler>() )
		{
			if ( type.IsAbstract || type.IsInterface )
				continue;

			if ( TypeLibrary.Create<IPhoneAppHandler>( type.TargetType ) is not { } handler )
				continue;

			var id = Normalize( handler.AppId );
			if ( string.IsNullOrWhiteSpace( id ) )
				continue;

			if ( _handlers.TryGetValue( id, out var existing ) )
			{
				Log.Warning( $"PhoneAppHandlerRegistry: duplicate handler for '{id}' ({existing.GetType().Name} / {handler.GetType().Name})." );
				continue;
			}

			_handlers[id] = handler;
		}
	}

	private static string Normalize( string value ) => (value ?? "").Trim();
}
