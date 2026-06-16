using System;
using System.Collections.Generic;
using Sandbox;

namespace Minimal.Clan;

public static partial class ClanDatabase
{
	public static List<Clan> Load()
	{
		if ( !Networking.IsHost )
			return new List<Clan>();

		try
		{
			FileSystem.Data.CreateDirectory( SaveFolder );
			if ( !FileSystem.Data.FileExists( SavePath ) )
				return new List<Clan>();

			var save = FileSystem.Data.ReadJsonOrDefault<ClanDatabaseSave>( SavePath );
			var clans = save?.Clans ?? new List<Clan>();
			foreach ( var clan in clans )
			{
				clan?.Normalize();
			}

			clans.RemoveAll( x => x is null || x.Id < 0 || x.LeaderSteamId == 0L || string.IsNullOrWhiteSpace( x.Header ) );
			return clans;
		}
		catch ( Exception e )
		{
			Log.Warning( $"[Clan] Load failed: {e.Message}" );
			return new List<Clan>();
		}
	}

	public static void Save( IReadOnlyCollection<Clan> clans )
	{
		if ( !Networking.IsHost )
			return;

		try
		{
			FileSystem.Data.CreateDirectory( SaveFolder );
			FileSystem.Data.WriteJson( SavePath, new ClanDatabaseSave
			{
				Clans = clans is null ? new List<Clan>() : new List<Clan>( clans )
			} );
		}
		catch ( Exception e )
		{
			Log.Warning( $"[Clan] Save failed: {e.Message}" );
		}
	}
}
