using Sandbox;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

public sealed partial class PropCatalogSynchronizer
{
	private const string CacheFolder = "prop_catalog";
	private const string CachePath = CacheFolder + "/cloud_catalog.json";
	private const int PageSize = 200;

	partial void StartCloudSync()
	{
		if ( !Networking.IsHost || IsSynchronizing )
			return;

		_ = SynchronizeCloudAsync();
	}

	private async Task SynchronizeCloudAsync()
	{
		SetSyncStarted();

		try
		{
			var catalog = FindCatalog();
			if ( catalog is null )
				throw new InvalidOperationException( "PropCatalog resource was not found." );

			MergeCachedEntries( catalog );

			var organization = NormalizeOrganization( catalog.SourceOrganization );
			if ( string.IsNullOrWhiteSpace( organization ) )
				throw new InvalidOperationException( "PropCatalog SourceOrganization is empty." );

			// Make the migrated catalog available immediately while the larger cloud
			// discovery request is still running.
			MergeLegacyFacepunchDefinitions( catalog, organization );
			catalog.ApplyFilters();
			Sandbox.PropsMenu.HostBroadcastPropCatalog();

			var packages = await FindCloudModelsAsync( organization );
			ApplyCloudPackages( catalog, packages );
			MergeLegacyFacepunchDefinitions( catalog, organization );
			catalog.ApplyFilters();
			SaveCache( catalog, organization );

			SetSyncCompleted( packages.Count );
			Sandbox.PropsMenu.HostBroadcastPropCatalog();
			Log.Info( $"[PropCatalog] Synced {packages.Count} public model packages from '{organization}'. Catalog contains {catalog.Entries.Count} entries." );
		}
		catch ( Exception exception )
		{
			SetSyncFailed( exception.Message );
			Log.Warning( $"[PropCatalog] Cloud synchronization failed: {exception.Message}" );
		}
	}

	private async Task<List<Package>> FindCloudModelsAsync( string organization )
	{
		var typed = await FindCloudModelsByQueryAsync( $"org:{organization} type:model sort:updated" );
		if ( typed.Count > 0 )
			return typed;

		return await FindCloudModelsByQueryAsync( $"org:{organization} sort:updated" );
	}

	private async Task<List<Package>> FindCloudModelsByQueryAsync( string query )
	{
		var packages = new List<Package>();
		var maximum = Math.Clamp( MaximumPackages, PageSize, 20000 );

		for ( var skip = 0; skip < maximum; skip += PageSize )
		{
			var result = await Package.FindAsync( query, PageSize, skip );
			if ( result?.Packages is null )
				break;

			var page = result.Packages.ToList();
			if ( page.Count == 0 )
				break;

			foreach ( var package in page )
			{
				if ( IsUsableModelPackage( package ) )
					packages.Add( package );

				if ( packages.Count >= maximum )
					break;
			}

			if ( packages.Count >= maximum
				|| page.Count < PageSize
				|| skip + page.Count >= result.TotalCount )
				break;
		}

		return packages
			.GroupBy( GetPackageIdent, StringComparer.OrdinalIgnoreCase )
			.Select( group => group.First() )
			.ToList();
	}

	private static bool IsUsableModelPackage( Package package )
	{
		if ( package is null || !package.Public || package.Archived )
			return false;

		return !string.IsNullOrWhiteSpace( GetPackageIdent( package ) )
			&& (string.Equals( package.TypeName, "model", StringComparison.OrdinalIgnoreCase )
				|| GetPrimaryAsset( package ).EndsWith( ".vmdl", StringComparison.OrdinalIgnoreCase ));
	}

	private static void ApplyCloudPackages( PropCatalog catalog, IReadOnlyCollection<Package> packages )
	{
		catalog.Entries ??= new List<PropCatalogEntry>();

		foreach ( var entry in catalog.Entries.Where( entry => entry?.ImportedFromCloud == true ) )
			entry.CloudAvailable = false;

		foreach ( var package in packages )
		{
			var packageIdent = GetPackageIdent( package );
			var entry = FindByPackageIdent( catalog, packageIdent );
			if ( entry is null )
			{
				entry = CreateEntry( catalog, package );
				catalog.Entries.Add( entry );
			}

			UpdateCloudMetadata( entry, package );
		}

		catalog.Entries = catalog.Entries
			.Where( entry => entry is not null )
			.OrderBy( entry => entry.Category ?? "", StringComparer.OrdinalIgnoreCase )
			.ThenBy( entry => entry.Header ?? "", StringComparer.OrdinalIgnoreCase )
			.ThenBy( entry => entry.Id ?? "", StringComparer.OrdinalIgnoreCase )
			.ToList();
	}

	private static void MergeLegacyFacepunchDefinitions( PropCatalog catalog, string organization )
	{
		catalog.Entries ??= new List<PropCatalogEntry>();
		var prefix = organization + ".";

		foreach ( var legacy in ResourceLibrary.GetAll<PropDefinition>().Where( definition => definition is not null ) )
		{
			var packageIdent = (legacy.Ident ?? "").Trim();
			if ( !packageIdent.StartsWith( prefix, StringComparison.OrdinalIgnoreCase ) )
				continue;

			var entry = FindByPackageIdent( catalog, packageIdent );
			if ( entry is null )
			{
				entry = new PropCatalogEntry
				{
					Id = packageIdent,
					PackageIdent = packageIdent,
					Header = string.IsNullOrWhiteSpace( legacy.Header ) ? packageIdent : legacy.Header,
					Category = string.IsNullOrWhiteSpace( legacy.Category ) ? "Other" : legacy.Category,
					Description = legacy.Description ?? "",
					Price = legacy.Price,
					PolicyMode = PropCatalogPolicyMode.Automatic,
					AutoCategory = false,
					PhysicsMode = PropPhysicsMode.Dynamic,
					ImportedFromCloud = true
				};
				catalog.Entries.Add( entry );
			}

			entry.CloudAvailable = true;
		}
	}

	private static PropCatalogEntry CreateEntry( PropCatalog catalog, Package package )
	{
		var enabled = catalog.EnableNewEntriesByDefault;
		return new PropCatalogEntry
		{
			Id = GetPackageIdent( package ),
			PackageIdent = GetPackageIdent( package ),
			Header = GetPackageTitle( package ),
			Category = "Other",
			Enabled = enabled,
			VisibleInMenu = enabled,
			Spawnable = enabled,
				PhysicsMode = PropPhysicsMode.Dynamic,
				PolicyMode = PropCatalogPolicyMode.Automatic,
				AutoCategory = true,
				ImportedFromCloud = true,
			CloudAvailable = true
		};
	}

	private static void UpdateCloudMetadata( PropCatalogEntry entry, Package package )
	{
		entry.PackageIdent = GetPackageIdent( package );
		entry.AssetPath = GetPrimaryAsset( package );
		entry.ThumbnailUrl = package.Thumb ?? "";
		entry.ImportedFromCloud = true;
		entry.CloudAvailable = true;
		entry.CloudUpdatedUtc = package.Updated.UtcDateTime.ToString( "O" );
		entry.Tags = package.Tags?.Where( tag => !string.IsNullOrWhiteSpace( tag ) ).Distinct().OrderBy( tag => tag ).ToList() ?? new List<string>();

		if ( string.IsNullOrWhiteSpace( entry.Id ) )
			entry.Id = entry.PackageIdent;
		if ( string.IsNullOrWhiteSpace( entry.Header ) || string.Equals( entry.Header, "Unknown", StringComparison.OrdinalIgnoreCase ) )
			entry.Header = GetPackageTitle( package );
		if ( string.IsNullOrWhiteSpace( entry.Description ) )
			entry.Description = package.Summary ?? "";
	}

	private static PropCatalogEntry FindByPackageIdent( PropCatalog catalog, string packageIdent )
	{
		return catalog.Entries.FirstOrDefault( entry => entry is not null &&
			(string.Equals( entry.PackageIdent, packageIdent, StringComparison.OrdinalIgnoreCase )
			|| string.Equals( entry.Id, packageIdent, StringComparison.OrdinalIgnoreCase )) );
	}

	private static string GetPackageIdent( Package package )
	{
		return (package?.FullIdent ?? package?.Ident ?? "").Trim();
	}

	private static string GetPrimaryAsset( Package package )
	{
		if ( package is null )
			return "";

		return string.IsNullOrWhiteSpace( package.PrimaryAsset )
			? package.GetMeta( "PrimaryAsset", "" )
			: package.PrimaryAsset;
	}

	private static string GetPackageTitle( Package package )
	{
		var title = (package?.Title ?? "").Trim();
		return string.IsNullOrWhiteSpace( title ) ? GetPackageIdent( package ) : title;
	}

	private static string NormalizeOrganization( string organization )
	{
		return (organization ?? "").Trim().ToLowerInvariant();
	}

	private static void MergeCachedEntries( PropCatalog catalog )
	{
		catalog.Entries ??= new List<PropCatalogEntry>();
		if ( !FileSystem.Data.FileExists( CachePath ) )
			return;

		var cache = FileSystem.Data.ReadJsonOrDefault<PropCatalogCacheData>( CachePath );
		if ( cache?.Entries is null )
			return;

		foreach ( var cachedEntry in cache.Entries.Where( entry => entry is not null ) )
		{
			if ( FindByPackageIdent( catalog, cachedEntry.PackageIdent ) is null )
				catalog.Entries.Add( cachedEntry );
		}
	}

	private static void SaveCache( PropCatalog catalog, string organization )
	{
		FileSystem.Data.CreateDirectory( CacheFolder );
		FileSystem.Data.WriteJson( CachePath, new PropCatalogCacheData
		{
			SourceOrganization = organization,
			LastSuccessfulSyncUtc = DateTime.UtcNow.ToString( "O" ),
			Entries = catalog.Entries.ToList()
		} );
	}
}
