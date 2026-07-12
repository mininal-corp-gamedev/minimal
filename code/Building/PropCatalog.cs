using Sandbox;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Central catalog of props available to Minimal RP.
/// Cloud synchronization populates this resource; the menu and the server will
/// consume it in later stages.
/// </summary>
[AssetType( Name = "Prop Catalog", Extension = "pcatalog", Category = "Minimal" )]
public sealed class PropCatalog : GameResource
{
	private static PropCatalog _runtimeFallback;

	public static PropCatalog GetCurrent()
	{
		var resource = ResourceLibrary.GetAll<PropCatalog>()
			.FirstOrDefault( catalog => catalog is not null );
		return resource ??= _runtimeFallback ??= new PropCatalog();
	}

	/// <summary>Cloud organization used by the future catalog synchronizer.</summary>
	[Property] public string SourceOrganization { get; set; } = "facepunch";

	/// <summary>
	/// Safe default for newly discovered cloud entries. Keeping this disabled
	/// prevents new cloud assets from appearing to players without approval.
	/// </summary>
	[Property] public bool EnableNewEntriesByDefault { get; set; } = false;
	[Property] public PropCatalogFilterSettings Filters { get; set; } = new();

	[Property] public List<PropCatalogEntry> Entries { get; set; } = new();

	public PropCatalogEntry Find( string id )
	{
		var normalized = NormalizeId( id );
		if ( string.IsNullOrWhiteSpace( normalized ) )
			return null;

		return Entries?.FirstOrDefault( entry =>
			entry is not null
			&& string.Equals( NormalizeId( entry.Id ), normalized, StringComparison.OrdinalIgnoreCase ) );
	}

	public IEnumerable<PropCatalogEntry> GetEnabledEntries()
	{
		return (Entries ?? new List<PropCatalogEntry>())
			.Where( entry => entry is not null && entry.Enabled );
	}

	public IEnumerable<PropCatalogEntry> GetPlayerVisibleEntries()
	{
		return GetEnabledEntries()
			.Where( entry => entry.VisibleInMenu && entry.Spawnable && !entry.AdminOnly );
	}

	/// <summary>
	/// Re-evaluates automatic entries. Entries switched to Manual keep their
	/// Enabled, VisibleInMenu and Spawnable values unchanged.
	/// </summary>
	public void ApplyFilters()
	{
		Entries ??= new List<PropCatalogEntry>();
		Filters ??= new PropCatalogFilterSettings();

		foreach ( var entry in Entries.Where( entry => entry is not null ) )
		{
			ApplyAutomaticCategory( entry );

			if ( entry.PolicyMode == PropCatalogPolicyMode.Manual )
			{
				entry.FilterReason = "Manual settings";
				entry.AutoFilterAllowed = entry.Enabled;
				continue;
			}

			var result = EvaluateAutomaticFilter( entry );
			entry.AutoFilterAllowed = result.Allowed;
			entry.FilterReason = result.Reason;

			var enabled = result.Allowed
				&& (EnableNewEntriesByDefault || Filters.IsExplicitlyIncluded( entry ));
			entry.Enabled = enabled;
			entry.VisibleInMenu = enabled;
			entry.Spawnable = enabled;
		}
	}

	public PropCatalogFilterResult EvaluateAutomaticFilter( PropCatalogEntry entry )
	{
		Filters ??= new PropCatalogFilterSettings();
		Filters.EnsureInitialized();

		if ( entry is null )
			return PropCatalogFilterResult.Blocked( "Missing catalog entry" );

		if ( entry.ImportedFromCloud && !entry.CloudAvailable )
			return PropCatalogFilterResult.Blocked( "Cloud package is unavailable" );

		var packageIdent = Normalize( entry.PackageIdent );
		if ( Filters.ExcludedPackageIdents.Any( value =>
			string.Equals( Normalize( value ), packageIdent, StringComparison.OrdinalIgnoreCase ) ) )
			return PropCatalogFilterResult.Blocked( "Excluded package ident" );

		var assetPath = Normalize( entry.AssetPath );
		if ( Filters.ExcludedAssetPathPrefixes.Any( prefix =>
			assetPath.StartsWith( Normalize( prefix ), StringComparison.OrdinalIgnoreCase ) ) )
			return PropCatalogFilterResult.Blocked( "Excluded asset path" );

		var tags = entry.Tags ?? new List<string>();
		if ( Filters.ExcludedTags.Any( excluded => tags.Any( tag =>
			string.Equals( Normalize( tag ), Normalize( excluded ), StringComparison.OrdinalIgnoreCase ) ) ) )
			return PropCatalogFilterResult.Blocked( "Excluded cloud tag" );

		if ( Filters.RequiredTags.Count > 0 && !Filters.RequiredTags.Any( required => tags.Any( tag =>
			string.Equals( Normalize( tag ), Normalize( required ), StringComparison.OrdinalIgnoreCase ) ) ) )
			return PropCatalogFilterResult.Blocked( "Required cloud tag is missing" );

		var searchable = string.Join( " ", new[]
		{
			entry.Id,
			entry.PackageIdent,
			entry.AssetPath,
			entry.Header,
			entry.Description,
			string.Join( " ", tags )
		} ).ToLowerInvariant();

		var matchedPattern = Filters.ExcludedTextPatterns
			.Select( Normalize )
			.FirstOrDefault( pattern => !string.IsNullOrWhiteSpace( pattern ) && searchable.Contains( pattern ) );
		if ( !string.IsNullOrWhiteSpace( matchedPattern ) )
			return PropCatalogFilterResult.Blocked( $"Matched excluded text: {matchedPattern}" );

		return PropCatalogFilterResult.Passed();
	}

	private void ApplyAutomaticCategory( PropCatalogEntry entry )
	{
		Filters ??= new PropCatalogFilterSettings();
		Filters.EnsureInitialized();

		if ( !entry.AutoCategory || Filters.CategoryRules is null )
			return;

		var searchable = $"{entry.Id} {entry.PackageIdent} {entry.AssetPath} {entry.Header} {string.Join( " ", entry.Tags ?? new List<string>() )}"
			.ToLowerInvariant();

		foreach ( var rule in Filters.CategoryRules.Where( rule => rule is not null ) )
		{
			if ( (rule.MatchTerms ?? new List<string>()).Any( term =>
				!string.IsNullOrWhiteSpace( Normalize( term ) ) && searchable.Contains( Normalize( term ) ) ) )
			{
				entry.Category = string.IsNullOrWhiteSpace( rule.Category ) ? "Other" : rule.Category.Trim();
				return;
			}
		}

		entry.Category = "Other";
	}

	private static string Normalize( string value )
	{
		return (value ?? string.Empty).Trim().ToLowerInvariant();
	}

	public static string NormalizeId( string id )
	{
		return (id ?? string.Empty).Trim();
	}

	protected override Bitmap CreateAssetTypeIcon( int width, int height )
	{
		return CreateSimpleAssetTypeIcon( "list", width, height, "#ffffff", "#e08a24" );
	}
}

/// <summary>One cloud model and its Minimal RP policy.</summary>
public sealed class PropCatalogEntry
{
	/// <summary>Stable identifier used by favorites and spawn RPCs.</summary>
	[Property] public string Id { get; set; } = "";

	/// <summary>Cloud package identifier, for example facepunch.couch.</summary>
	[Property] public string PackageIdent { get; set; } = "";

	/// <summary>
	/// Optional explicit model path inside the mounted package. When empty, the
	/// package PrimaryAsset will be used.
	/// </summary>
	[Property] public string AssetPath { get; set; } = "";
	[Property] public string ThumbnailUrl { get; set; } = "";

	[Property] public string Header { get; set; } = "Unknown";
	[Property] public string Category { get; set; } = "Other";
	[Property] public string Description { get; set; } = "";
	[Property] public int Price { get; set; } = 0;
	[Property] public List<string> Tags { get; set; } = new();

	/// <summary>Master switch for this entry.</summary>
	[Property] public bool Enabled { get; set; } = false;

	/// <summary>Whether regular players can see this entry in the props menu.</summary>
	[Property] public bool VisibleInMenu { get; set; } = false;

	/// <summary>Server-side permission to spawn this entry.</summary>
	[Property] public bool Spawnable { get; set; } = false;

	[Property] public bool AdminOnly { get; set; } = false;
	[Property] public PropCatalogPolicyMode PolicyMode { get; set; } = PropCatalogPolicyMode.Automatic;
	[Property] public bool AutoCategory { get; set; } = true;
	[Property] public bool AutoFilterAllowed { get; set; } = false;
	[Property] public string FilterReason { get; set; } = "Not evaluated";

	/// <summary>True when this entry was discovered by the cloud synchronizer.</summary>
	[Property] public bool ImportedFromCloud { get; set; } = false;

	/// <summary>
	/// False means the package was not returned by the latest complete cloud
	/// synchronization. The entry is retained so manual policy is never lost.
	/// </summary>
	[Property] public bool CloudAvailable { get; set; } = true;

	[Property] public string CloudUpdatedUtc { get; set; } = "";

	[Property] public PropPhysicsMode PhysicsMode { get; set; } = PropPhysicsMode.Dynamic;

	/// <summary>Zero keeps the mass calculated from the model.</summary>
	[Property] public float MassOverride { get; set; } = 0f;
}

public enum PropPhysicsMode
{
	Dynamic,
	Frozen,
	Disabled
}

public enum PropCatalogPolicyMode
{
	Automatic,
	Manual
}

public sealed class PropCatalogFilterSettings
{
	/// <summary>Case-insensitive substrings checked against all prop metadata.</summary>
	[Property] public List<string> ExcludedTextPatterns { get; set; } = new()
	{
		"barrel", "oil_drum", "oildrum", "weapon", "explosive", "gib", "trigger", "effect"
	};
	[Property] public List<string> IncludedPackageIdents { get; set; } = new()
	{
		"facepunch.beech_hedge_40x32",
		"facepunch.beech_hedge_96x128",
		"facepunch.beech_hedge_96x64",
		"facepunch.coffeetablea",
		"facepunch.corrugated_wall_a_128",
		"facepunch.corrugated_wall_doorframe_a_128",
		"facepunch.corrugated_windowframe_a_128",
		"facepunch.fence_panel_large",
		"facepunch.filing_cabinet_03",
		"facepunch.industrial_shelving_01",
		"facepunch.metal_fence_panel",
		"facepunch.modern_shelving_a6",
		"facepunch.modern_shelving_b2",
		"facepunch.plastic_chair",
		"facepunch.washingmachine",
		"facepunch.wooden_chair_a",
		"facepunch.wooden_crate"
	};
	[Property] public List<string> ExcludedPackageIdents { get; set; } = new();
	[Property] public List<string> ExcludedAssetPathPrefixes { get; set; } = new()
	{
		"models/weapons/", "models/effects/", "particles/"
	};
	[Property] public List<string> ExcludedTags { get; set; } = new();

	/// <summary>When non-empty, an automatic entry must contain at least one required tag.</summary>
	[Property] public List<string> RequiredTags { get; set; } = new();

	[Property] public List<PropCatalogCategoryRule> CategoryRules { get; set; } = new()
	{
		new() { Category = "Furniture", MatchTerms = new() { "chair", "table", "couch", "sofa", "shelf", "cabinet", "bed" } },
		new() { Category = "Construction", MatchTerms = new() { "wall", "fence", "panel", "beam", "plank", "brick" } },
		new() { Category = "Street", MatchTerms = new() { "traffic", "cone", "sign", "bollard", "bench", "lamp" } },
		new() { Category = "Industrial", MatchTerms = new() { "industrial", "crate", "storage", "pipe", "tank", "machine" } },
		new() { Category = "Decoration", MatchTerms = new() { "plant", "pot", "picture", "poster", "decor", "statue" } }
	};

	public void EnsureInitialized()
	{
		ExcludedTextPatterns ??= new List<string>();
		IncludedPackageIdents ??= new List<string>();
		ExcludedPackageIdents ??= new List<string>();
		ExcludedAssetPathPrefixes ??= new List<string>();
		ExcludedTags ??= new List<string>();
		RequiredTags ??= new List<string>();
		CategoryRules ??= new List<PropCatalogCategoryRule>();
	}

	public bool IsExplicitlyIncluded( PropCatalogEntry entry )
	{
		EnsureInitialized();
		var packageIdent = (entry?.PackageIdent ?? string.Empty).Trim();
		return IncludedPackageIdents.Any( value =>
			string.Equals( (value ?? string.Empty).Trim(), packageIdent, StringComparison.OrdinalIgnoreCase ) );
	}
}

public sealed class PropCatalogCategoryRule
{
	[Property] public string Category { get; set; } = "Other";
	[Property] public List<string> MatchTerms { get; set; } = new();
}

public readonly struct PropCatalogFilterResult
{
	public bool Allowed { get; }
	public string Reason { get; }

	public PropCatalogFilterResult( bool allowed, string reason )
	{
		Allowed = allowed;
		Reason = reason;
	}

	public static PropCatalogFilterResult Passed() => new( true, "Passed automatic filters" );
	public static PropCatalogFilterResult Blocked( string reason ) => new( false, reason );
}
