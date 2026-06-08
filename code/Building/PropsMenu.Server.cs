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
	private const string PropPhysicsDebugPrefix = "[PropPhysicsDebug]";

	public sealed class PropFavoritesSaveData
	{
		public long SteamId { get; set; }
		public List<string> PropIds { get; set; } = new();
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

		if ( player.OwnedPropsCount >= player.MaxProps )
		{
			NotifySpawnCaller( caller, GameLocalization.Format( "notify.props.limit_reached", "Prop limit reached ({0}).", player.MaxProps ), false );
			return;
		}

		var normalized = (propId ?? string.Empty).Trim();
		var prop = ResourceLibrary
			.GetAll<PropDefinition>()
			.FirstOrDefault( x => x is not null && string.Equals( x.Id, normalized, StringComparison.Ordinal ) );

		if ( prop is null )
		{
			NotifySpawnCaller( caller, GameLocalization.Phrase( "notify.props.not_found", "Prop not found." ), false );
			return;
		}

		if ( string.IsNullOrWhiteSpace( prop.Ident ) )
		{
			NotifySpawnCaller( caller, GameLocalization.Phrase( "notify.props.no_cloud_ident", "Prop has no cloud ident." ), false );
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

		_ = SpawnCloudPropAsync( caller, player, prop, spawnPosition, Rotation.LookAt( yawForward ) );
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

	private static async Task SpawnCloudPropAsync( Connection caller, Player player, PropDefinition prop, Vector3 position, Rotation rotation )
	{
		try
		{
			var package = await Package.Fetch( prop.Ident, false );
			if ( package is null || package.Revision is null )
			{
				NotifySpawnCaller( caller, GameLocalization.Phrase( "notify.props.cloud_package_not_found", "Cloud package not found." ), false );
				return;
			}

			await package.MountAsync();

			var primaryAsset = package.GetMeta<string>( "PrimaryAsset" );
			if ( string.IsNullOrWhiteSpace( primaryAsset ) )
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

			if ( !player.IsValid() )
				return;

			if ( player.OwnedPropsCount >= player.MaxProps )
			{
				NotifySpawnCaller( caller, GameLocalization.Format( "notify.props.limit_reached", "Prop limit reached ({0}).", player.MaxProps ), false );
				return;
			}

			var gameObj = new GameObject( true, GetPropHeader( prop ) );
			gameObj.WorldPosition = position;
			gameObj.WorldRotation = rotation;
			gameObj.Tags.Add( PropCollisionTags.PropTag );

			var renderer = gameObj.Components.Create<ModelRenderer>();
			renderer.Model = model;

			var modelCollider = gameObj.Components.Create<ModelCollider>();
			modelCollider.Model = model;
			modelCollider.Static = false;

			var rb = gameObj.Components.Create<Rigidbody>();
			rb.MotionEnabled = true;
			rb.StartAsleep = false;
			rb.EnhancedCcd = true;

			var bodyValid = rb.PhysicsBody is not null && rb.PhysicsBody.IsValid();
			PropCollisionTags.TryRefreshPhysicsShapeTags( gameObj, out var shapeCount );
			Log.Info( $"{PropPhysicsDebugPrefix} spawn object='{gameObj.Name}' model='{primaryAsset}' colliderValid={modelCollider.IsValid()} rbValid={rb.IsValid()} bodyValid={bodyValid} shapes={shapeCount} motion={rb.MotionEnabled} startAsleep={rb.StartAsleep} ccd={rb.EnhancedCcd}" );

			var propCustom = gameObj.Components.Create<PropCustom>();
			propCustom.SetOwner( player );
			player.RegisterSpawnedProp( propCustom );

			gameObj.NetworkSpawn();
			OwnedPropNetwork.ConfigurePropCustom( gameObj );

			Log.Info( $"[PropsMenu] {caller.DisplayName} spawned cloud prop '{GetPropHeader( prop )}' (ident: {prop.Ident})." );
			NotifySpawnCaller( caller, GameLocalization.Format( "notify.props.spawned", "Spawned: {0}.", GetPropHeader( prop ) ), true );
		}
		catch ( Exception e )
		{
			Log.Warning( $"[PropsMenu] Cloud spawn failed for '{prop?.Ident}': {e.Message}" );
			NotifySpawnCaller( caller, GameLocalization.Phrase( "notify.props.cloud_spawn_failed", "Cloud spawn failed." ), false );
		}
	}

	private static void NotifySpawnCaller( Connection caller, string text, bool success )
	{
		using ( Rpc.FilterInclude( x => x.SteamId.Value == caller.SteamId.Value ) )
		{
			RpcReceiveSpawnResult( text, success );
		}
	}
}
