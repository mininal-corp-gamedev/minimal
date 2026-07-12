using Sandbox;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Sandbox;

public sealed partial class PropsMenu
{
	private const float SpawnTraceDistance = 420f;
	private const float FallbackSpawnDistance = 120f;
	private const float SpawnSurfaceOffset = 24f;
	private const string PropFavoritesFolder = "props_favorites";
	private static readonly Dictionary<long, int> PendingPropSpawns = new();

	public sealed class PropFavoritesSaveData
	{
		public long SteamId { get; set; }
		public List<string> PropIds { get; set; } = new();
	}

	private static void RpcRequestPropCatalogServer()
	{
		if ( !Networking.IsHost )
			return;

		var caller = Rpc.Caller;
		if ( caller is null )
			return;

		SendPropCatalog( caller );
	}

	public static void HostBroadcastPropCatalog()
	{
		if ( !Networking.IsHost )
			return;

		RpcReceivePropCatalog( SerializeVisibleCatalog() );
	}

	private static void RpcRequestPropFavoritesServer()
	{
		if ( !Networking.IsHost )
			return;

		var caller = Rpc.Caller;
		if ( caller is null )
			return;

		SendPropFavorites( caller, LoadPropFavorites( caller.SteamId.Value ).PropIds );
	}

	private static void RpcSetPropFavoriteServer( string propId, bool favorite )
	{
		if ( !Networking.IsHost )
			return;

		var caller = Rpc.Caller;
		if ( caller is null )
			return;

		var normalized = (propId ?? string.Empty).Trim();
		if ( string.IsNullOrWhiteSpace( normalized ) )
			return;

		var data = LoadPropFavorites( caller.SteamId.Value );
		data.SteamId = caller.SteamId.Value;
		data.PropIds ??= new();

		if ( favorite )
		{
			if ( !data.PropIds.Any( id => string.Equals( id, normalized, StringComparison.Ordinal ) ) )
				data.PropIds.Add( normalized );
		}
		else
		{
			data.PropIds.RemoveAll( id => string.Equals( id, normalized, StringComparison.Ordinal ) );
		}

		data.PropIds = data.PropIds
			.Where( id => !string.IsNullOrWhiteSpace( id ) )
			.Select( id => id.Trim() )
			.Distinct( StringComparer.Ordinal )
			.OrderBy( id => id, StringComparer.Ordinal )
			.ToList();

		SavePropFavorites( data );
		SendPropFavorites( caller, data.PropIds );
	}

	private static void RpcRequestSpawnPropServer( string propId, Vector3 eyePosition, Vector3 eyeForward )
	{
		if ( !Networking.IsHost )
			return;

		var caller = Rpc.Caller;
		if ( caller is null )
			return;

		var player = Player.FindPlayerBySteamId( caller.SteamId.Value );
		if ( !player.IsValid() || player.GameObject.Network.Owner != caller )
		{
			NotifySpawnCaller( caller, GameLocalization.Phrase( "notify.player.not_ready", "Your player is not ready." ), false );
			return;
		}

		if ( player.IsArrested )
		{
			NotifySpawnCaller( caller, GameLocalization.Phrase( "notify.player.arrested", "You are arrested." ), false );
			return;
		}

		if ( player.Job?.JobDefinition?.CanSpawnProp == false )
		{
			NotifySpawnCaller( caller, GameLocalization.Phrase( "notify.props.job_cannot_spawn", "Your job cannot spawn props." ), false );
			return;
		}

		if ( player.OwnedPropsCount >= player.MaxProps )
		{
			NotifySpawnCaller( caller, GameLocalization.Format( "notify.props.limit_reached", "Prop limit reached ({0}).", player.MaxProps ), false );
			return;
		}

		var normalized = (propId ?? string.Empty).Trim();
		var catalog = GetPropCatalog();
		var prop = catalog?.Find( normalized );

		if ( prop is null
			|| !prop.Enabled
			|| !prop.Spawnable
			|| !prop.CloudAvailable
			|| (!prop.VisibleInMenu && player.AdminRank <= 0) )
		{
			NotifySpawnCaller( caller, GameLocalization.Phrase( "notify.props.not_found", "Prop not found." ), false );
			return;
		}

		if ( prop.AdminOnly && player.AdminRank <= 0 )
		{
			NotifySpawnCaller( caller, GameLocalization.Phrase( "notify.props.not_found", "Prop not found." ), false );
			return;
		}

		if ( string.IsNullOrWhiteSpace( prop.PackageIdent ) )
		{
			NotifySpawnCaller( caller, GameLocalization.Phrase( "notify.props.no_cloud_ident", "Prop has no cloud ident." ), false );
			return;
		}

		if ( !IsPackageAllowedByCatalogSource( catalog, prop.PackageIdent ) )
		{
			NotifySpawnCaller( caller, GameLocalization.Phrase( "notify.props.not_found", "Prop not found." ), false );
			return;
		}

		var forward = eyeForward.LengthSquared > 0.001f ? eyeForward.Normal : player.WorldRotation.Forward;
		if ( Vector3.DistanceBetween( player.WorldPosition, eyePosition ) > 220f )
			eyePosition = player.WorldPosition;

		var trace = player.Scene.Trace
			.Ray( eyePosition, eyePosition + forward * SpawnTraceDistance )
			.IgnoreGameObjectHierarchy( player.GameObject )
			.WithoutTags( "player", "bullet" )
			.Run();

		var spawnPosition = trace.Hit
			? trace.HitPosition + trace.Normal * SpawnSurfaceOffset
			: player.WorldPosition + forward * FallbackSpawnDistance + Vector3.Up * SpawnSurfaceOffset;

		var yawForward = new Vector3( forward.x, forward.y, 0f );
		if ( yawForward.LengthSquared <= 0.001f )
			yawForward = player.WorldRotation.Forward;
		yawForward = yawForward.Normal;

		if ( !TryReservePropSpawn( player, caller.SteamId.Value ) )
		{
			NotifySpawnCaller( caller, GameLocalization.Format( "notify.props.limit_reached", "Prop limit reached ({0}).", player.MaxProps ), false );
			return;
		}

		_ = SpawnCloudPropAsync( caller, player, prop, spawnPosition, Rotation.LookAt( yawForward ), caller.SteamId.Value );
	}

	private static void SendPropFavorites( Connection caller, IEnumerable<string> propIds )
	{
		if ( caller is null )
			return;

		var json = JsonSerializer.Serialize( propIds?.ToList() ?? new List<string>() );
		using ( Rpc.FilterInclude( x => x.SteamId.Value == caller.SteamId.Value ) )
		{
			RpcReceivePropFavorites( json );
		}
	}

	private static void SendPropCatalog( Connection caller )
	{
		if ( caller is null )
			return;

		using ( Rpc.FilterInclude( connection => connection.SteamId.Value == caller.SteamId.Value ) )
		{
			RpcReceivePropCatalog( SerializeVisibleCatalog( caller ) );
		}
	}

	private static string SerializeVisibleCatalog( Connection caller = null )
	{
		var catalog = GetPropCatalog();
		if ( catalog is null )
			return "[]";

		var callerPlayer = caller is null ? null : Player.FindPlayerBySteamId( caller.SteamId.Value );
		var isAdmin = callerPlayer.IsValid() && callerPlayer.AdminRank > 0;
		var entries = catalog.GetEnabledEntries()
			.Where( entry => entry.VisibleInMenu && entry.Spawnable && entry.CloudAvailable )
			.Where( entry => !entry.AdminOnly || isAdmin )
			.Select( entry => new PropMenuEntry
			{
				Id = entry.Id,
				Header = entry.Header,
				Category = entry.Category,
				Description = entry.Description,
				Price = entry.Price,
				PackageIdent = entry.PackageIdent,
				ThumbnailUrl = entry.ThumbnailUrl
			} )
			.ToList();

		return JsonSerializer.Serialize( entries );
	}

	private static PropCatalog GetPropCatalog()
	{
		return PropCatalog.GetCurrent();
	}

	private static void EnsurePropFavoritesFolder()
	{
		FileSystem.Data.CreateDirectory( PropFavoritesFolder );
	}

	private static string GetPropFavoritesPath( long steamId )
	{
		return $"{PropFavoritesFolder}/{steamId}.json";
	}

	private static PropFavoritesSaveData LoadPropFavorites( long steamId )
	{
		EnsurePropFavoritesFolder();

		try
		{
			var path = GetPropFavoritesPath( steamId );
			if ( !FileSystem.Data.FileExists( path ) )
				return new PropFavoritesSaveData { SteamId = steamId };

			var data = FileSystem.Data.ReadJsonOrDefault<PropFavoritesSaveData>( path );
			if ( data is null )
				return new PropFavoritesSaveData { SteamId = steamId };

			data.SteamId = steamId;
			data.PropIds ??= new();
			return data;
		}
		catch ( Exception e )
		{
			Log.Warning( $"[PropsMenu] Failed to load favorites for {steamId}: {e.Message}" );
			return new PropFavoritesSaveData { SteamId = steamId };
		}
	}

	private static void SavePropFavorites( PropFavoritesSaveData data )
	{
		if ( data is null || data.SteamId <= 0 )
			return;

		EnsurePropFavoritesFolder();
		FileSystem.Data.WriteJson( GetPropFavoritesPath( data.SteamId ), data );
	}

	private static async Task SpawnCloudPropAsync( Connection caller, Player player, PropCatalogEntry prop, Vector3 position, Rotation rotation, long reservationSteamId )
	{
		try
		{
			var catalog = GetPropCatalog();
			if ( !IsPackageAllowedByCatalogSource( catalog, prop.PackageIdent ) )
				return;

			var package = await Package.Fetch( prop.PackageIdent, false );
			if ( package is null || package.Revision is null || !package.Public || package.Archived )
			{
				NotifySpawnCaller( caller, GameLocalization.Phrase( "notify.props.cloud_package_not_found", "Cloud package not found." ), false );
				return;
			}

			await package.MountAsync();

			var primaryAsset = string.IsNullOrWhiteSpace( prop.AssetPath )
				? package.GetMeta( "PrimaryAsset", package.PrimaryAsset ?? "" )
				: prop.AssetPath;
			if ( !IsSafeModelAssetPath( primaryAsset ) )
			{
				NotifySpawnCaller( caller, GameLocalization.Phrase( "notify.props.no_primary_asset", "Package has no primary asset." ), false );
				return;
			}

			var model = Model.Load( primaryAsset );
			if ( model is null || model.IsError )
			{
				NotifySpawnCaller( caller, GameLocalization.Phrase( "notify.props.failed_model", "Failed to load model." ), false );
				return;
			}

			if ( !player.IsValid() || caller is null || player.GameObject.Network.Owner != caller )
				return;

			// Re-resolve the authoritative entry after asynchronous package loading.
			// Catalog rules may have changed while the request was in flight.
			var currentEntry = GetPropCatalog()?.Find( prop.Id );
			if ( currentEntry is null
				|| !currentEntry.Enabled
				|| !currentEntry.Spawnable
				|| !currentEntry.CloudAvailable
				|| (!currentEntry.VisibleInMenu && player.AdminRank <= 0)
				|| !string.Equals( currentEntry.PackageIdent, prop.PackageIdent, StringComparison.OrdinalIgnoreCase ) )
				return;
			prop = currentEntry;

			if ( player.IsArrested )
			{
				NotifySpawnCaller( caller, GameLocalization.Phrase( "notify.player.arrested", "You are arrested." ), false );
				return;
			}

			if ( player.Job?.JobDefinition?.CanSpawnProp == false )
			{
				NotifySpawnCaller( caller, GameLocalization.Phrase( "notify.props.job_cannot_spawn", "Your job cannot spawn props." ), false );
				return;
			}

			if ( prop.AdminOnly && player.AdminRank <= 0 )
				return;

			if ( player.OwnedPropsCount >= player.MaxProps )
			{
				NotifySpawnCaller( caller, GameLocalization.Format( "notify.props.limit_reached", "Prop limit reached ({0}).", player.MaxProps ), false );
				return;
			}

			var gameObj = new GameObject( true, GetCatalogPropHeader( prop ) );
			gameObj.WorldPosition = position;
			gameObj.WorldRotation = rotation;

			var propComponent = gameObj.Components.Create<Sandbox.Prop>();
			propComponent.Model = model;
			propComponent.Health = 999999f;

			var propCustom = gameObj.Components.Create<PropCustom>();
			propCustom.SetOwner( player );
			propCustom.ConfigureInitialPhysics( prop.PhysicsMode, prop.MassOverride );
			player.RegisterSpawnedProp( propCustom );

			gameObj.Tags.Add( "prop" );
			gameObj.NetworkSpawn();
			OwnedPropNetwork.ConfigurePropCustom( gameObj );

			Log.Info( $"[PropsMenu] {caller.DisplayName} spawned cloud prop '{GetCatalogPropHeader( prop )}' (ident: {prop.PackageIdent})." );
			NotifySpawnCaller( caller, GameLocalization.Format( "notify.props.spawned", "Spawned: {0}.", GetCatalogPropHeader( prop ) ), true );
		}
		catch ( Exception e )
		{
			Log.Warning( $"[PropsMenu] Cloud spawn failed for '{prop?.PackageIdent}': {e.Message}" );
			NotifySpawnCaller( caller, GameLocalization.Phrase( "notify.props.cloud_spawn_failed", "Cloud spawn failed." ), false );
		}
		finally
		{
			ReleasePropSpawnReservation( reservationSteamId );
		}
	}

	private static bool TryReservePropSpawn( Player player, long steamId )
	{
		if ( !player.IsValid() || steamId <= 0 )
			return false;

		PendingPropSpawns.TryGetValue( steamId, out var pending );
		if ( player.OwnedPropsCount + pending >= player.MaxProps )
			return false;

		PendingPropSpawns[steamId] = pending + 1;
		return true;
	}

	private static void ReleasePropSpawnReservation( long steamId )
	{
		if ( steamId <= 0 || !PendingPropSpawns.TryGetValue( steamId, out var pending ) )
			return;

		if ( pending <= 1 )
			PendingPropSpawns.Remove( steamId );
		else
			PendingPropSpawns[steamId] = pending - 1;
	}

	private static bool IsPackageAllowedByCatalogSource( PropCatalog catalog, string packageIdent )
	{
		var organization = (catalog?.SourceOrganization ?? "facepunch").Trim();
		var ident = (packageIdent ?? "").Trim();
		return !string.IsNullOrWhiteSpace( organization )
			&& ident.StartsWith( organization + ".", StringComparison.OrdinalIgnoreCase );
	}

	private static bool IsSafeModelAssetPath( string assetPath )
	{
		var path = (assetPath ?? "").Trim();
		return !string.IsNullOrWhiteSpace( path )
			&& path.EndsWith( ".vmdl", StringComparison.OrdinalIgnoreCase )
			&& !path.Contains( "..", StringComparison.Ordinal );
	}

	private static string GetCatalogPropHeader( PropCatalogEntry prop )
	{
		if ( prop is null )
			return "Unknown";
		return string.IsNullOrWhiteSpace( prop.Header ) ? prop.Id : prop.Header;
	}

	private static void NotifySpawnCaller( Connection caller, string text, bool success )
	{
		using ( Rpc.FilterInclude( x => x.SteamId.Value == caller.SteamId.Value ) )
		{
			RpcReceiveSpawnResult( text, success );
		}
	}
}
