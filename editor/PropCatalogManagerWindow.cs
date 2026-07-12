using Sandbox;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Editor;

[EditorApp( "Prop Catalog Manager", "inventory_2", "Manage Facepunch Cloud props" )]
public sealed class PropCatalogManagerWindow : Window
{
	private const string CatalogPath = "props/prop_catalog.pcatalog";
	private const int PageSize = 200;
	private const int MaximumPackages = 5000;
	private const int MaximumRenderedRows = 300;

	private Asset _catalogAsset;
	private PropCatalog _catalog;
	private LineEdit _search;
	private Label _status;
	private Label _count;
	private ScrollArea _scroll;
	private Button _syncButton;
	private Button _allTabButton;
	private Button _enabledTabButton;
	private bool _showEnabledOnly;
	private bool _syncing;

	public PropCatalogManagerWindow()
	{
		WindowTitle = "Prop Catalog Manager";
		SetWindowIcon( "inventory_2" );
		Size = new Vector2( 1180, 760 );
		MinimumSize = new Vector2( 820, 560 );
		DeleteOnClose = true;

		Canvas = new Widget( this );
		Canvas.Layout = Layout.Column();
		Canvas.Layout.Margin = 12;
		Canvas.Layout.Spacing = 8;

		BuildToolbar();
		BuildList();
		LoadCatalog();
		Show();
	}

	private void BuildToolbar()
	{
		var titleRow = Canvas.Layout.AddRow();
		titleRow.Spacing = 8;
		titleRow.Add( new Label.Subtitle( "Facepunch Cloud Props" ) );
		titleRow.AddStretchCell();

		_count = titleRow.Add( new Label( "0 props" ) );

		var tabs = Canvas.Layout.AddRow();
		tabs.Spacing = 4;

		_allTabButton = tabs.Add( new Button( "All props", "apps", this ) );
		_allTabButton.Clicked = () => SelectTab( false );

		_enabledTabButton = tabs.Add( new Button( "Enabled", "check_circle", this ) );
		_enabledTabButton.Clicked = () => SelectTab( true );
		tabs.AddStretchCell();
		UpdateTabButtons();

		var toolbar = Canvas.Layout.AddRow();
		toolbar.Spacing = 8;

		_search = toolbar.Add( new LineEdit( this )
		{
			PlaceholderText = "Search title, ident, category or filter reason...",
			MinimumWidth = 360
		}, 1 );
		_search.TextEdited += _ => RebuildRows();

		var reload = toolbar.Add( new Button( "Reload", "refresh", this ) );
		reload.Clicked = LoadCatalog;

		var enableFiltered = toolbar.Add( new Button( "Enable filtered", "visibility", this ) );
		enableFiltered.Clicked = () => SetFilteredEntriesEnabled( true );

		var disableFiltered = toolbar.Add( new Button( "Disable filtered", "visibility_off", this ) );
		disableFiltered.Clicked = () => SetFilteredEntriesEnabled( false );

		_syncButton = toolbar.Add( new Button.Primary( "Sync Facepunch Cloud", "cloud_sync", this ) );
		_syncButton.Clicked = () => _ = SynchronizeCloudAsync();

		_status = Canvas.Layout.Add( new Label( "Loading catalog..." ) );
		_status.WordWrap = true;
	}

	private void BuildList()
	{
		_scroll = Canvas.Layout.Add( new ScrollArea( this ), 1 );
		_scroll.HorizontalScrollbarMode = ScrollbarMode.Off;
		_scroll.Canvas = new Widget( _scroll );
		_scroll.Canvas.Layout = Layout.Column();
		_scroll.Canvas.Layout.Spacing = 4;
		_scroll.Canvas.Layout.Margin = new Sandbox.UI.Margin( 0, 0, 8, 0 );
		_scroll.Canvas.VerticalSizeMode = SizeMode.Flexible;
		_scroll.Canvas.HorizontalSizeMode = SizeMode.Flexible;
	}

	private void LoadCatalog()
	{
		_catalogAsset = AssetSystem.FindByPath( CatalogPath )
			?? AssetSystem.All.FirstOrDefault( asset =>
				asset.RelativePath?.EndsWith( CatalogPath, StringComparison.OrdinalIgnoreCase ) == true );

		if ( _catalogAsset is null )
		{
			_catalog = null;
			_status.Text = $"Catalog asset '{CatalogPath}' was not found. Wait for asset compilation and reopen the editor app.";
			RebuildRows();
			return;
		}

		_catalog = _catalogAsset.LoadResource<PropCatalog>();
		if ( _catalog is null )
		{
			_status.Text = "The catalog asset exists but could not be loaded as PropCatalog.";
			RebuildRows();
			return;
		}

		_catalog.Entries ??= new List<PropCatalogEntry>();
		MergeLegacyDefinitions();
		_catalog.ApplyFilters();
		SaveCatalog( rebuild: false );
		_status.Text = "Catalog loaded. Use Sync Facepunch Cloud to discover all public Facepunch models.";
		RebuildRows();
	}

	private void MergeLegacyDefinitions()
	{
		if ( _catalog is null )
			return;

		var organization = (_catalog.SourceOrganization ?? "facepunch").Trim();
		var prefix = organization + ".";

		foreach ( var legacy in ResourceLibrary.GetAll<PropDefinition>().Where( value => value is not null ) )
		{
			var ident = (legacy.Ident ?? "").Trim();
			if ( !ident.StartsWith( prefix, StringComparison.OrdinalIgnoreCase ) )
				continue;

			var entry = FindByPackageIdent( ident );
			if ( entry is null )
			{
				entry = new PropCatalogEntry
				{
					Id = ident,
					PackageIdent = ident,
					Header = string.IsNullOrWhiteSpace( legacy.Header ) ? ident : legacy.Header,
					Category = string.IsNullOrWhiteSpace( legacy.Category ) ? "Other" : legacy.Category,
					Description = legacy.Description ?? "",
					Price = legacy.Price,
					PolicyMode = PropCatalogPolicyMode.Automatic,
					AutoCategory = false,
					ImportedFromCloud = true,
					CloudAvailable = true,
					PhysicsMode = PropPhysicsMode.Dynamic
				};
				_catalog.Entries.Add( entry );
			}
		}
	}

	private async Task SynchronizeCloudAsync()
	{
		if ( _syncing || _catalog is null )
			return;

		_syncing = true;
		_syncButton.Enabled = false;
		_status.Text = "Querying sbox.game...";

		try
		{
			var organization = (_catalog.SourceOrganization ?? "facepunch").Trim().ToLowerInvariant();
			var packages = await FindPackagesAsync( $"org:{organization} type:model sort:updated" );
			if ( packages.Count == 0 )
				packages = await FindPackagesAsync( $"org:{organization} sort:updated" );

			foreach ( var entry in _catalog.Entries.Where( entry => entry?.ImportedFromCloud == true ) )
				entry.CloudAvailable = false;

			foreach ( var package in packages )
				MergePackage( package );

			MergeLegacyDefinitions();
			_catalog.ApplyFilters();
			_catalog.Entries = _catalog.Entries
				.Where( entry => entry is not null )
				.OrderBy( entry => entry.Category ?? "", StringComparer.OrdinalIgnoreCase )
				.ThenBy( entry => entry.Header ?? "", StringComparer.OrdinalIgnoreCase )
				.ToList();

			SaveCatalog( rebuild: false );
			_status.Text = $"Cloud sync complete: {packages.Count} model packages, {_catalog.Entries.Count} catalog entries.";
		}
		catch ( Exception exception )
		{
			_status.Text = $"Cloud sync failed: {exception.Message}";
			Log.Warning( $"[PropCatalogManager] Cloud sync failed: {exception}" );
		}
		finally
		{
			_syncing = false;
			_syncButton.Enabled = true;
			RebuildRows();
		}
	}

	private async Task<List<Package>> FindPackagesAsync( string query )
	{
		var packages = new List<Package>();

		for ( var skip = 0; skip < MaximumPackages; skip += PageSize )
		{
			_status.Text = $"Cloud sync: querying packages {skip + 1}-{skip + PageSize}...";
			var result = await Package.FindAsync( query, PageSize, skip );
			var page = result?.Packages?.ToList() ?? new List<Package>();
			if ( page.Count == 0 )
				break;

			packages.AddRange( page.Where( IsUsableModelPackage ) );
			if ( page.Count < PageSize || skip + page.Count >= result.TotalCount )
				break;
		}

		return packages
			.GroupBy( GetPackageIdent, StringComparer.OrdinalIgnoreCase )
			.Select( group => group.First() )
			.ToList();
	}

	private void MergePackage( Package package )
	{
		var ident = GetPackageIdent( package );
		var entry = FindByPackageIdent( ident );
		if ( entry is null )
		{
			entry = new PropCatalogEntry
			{
				Id = ident,
				PackageIdent = ident,
				Header = GetPackageTitle( package ),
				Category = "Other",
				PolicyMode = PropCatalogPolicyMode.Automatic,
				AutoCategory = true,
				PhysicsMode = PropPhysicsMode.Dynamic
			};
			_catalog.Entries.Add( entry );
		}

		entry.PackageIdent = ident;
		entry.AssetPath = GetPrimaryAsset( package );
		entry.ThumbnailUrl = package.Thumb ?? "";
		entry.ImportedFromCloud = true;
		entry.CloudAvailable = true;
		entry.CloudUpdatedUtc = package.Updated.UtcDateTime.ToString( "O" );
		entry.Tags = package.Tags?.Where( value => !string.IsNullOrWhiteSpace( value ) ).Distinct().OrderBy( value => value ).ToList() ?? new();

		if ( string.IsNullOrWhiteSpace( entry.Header ) || entry.Header == "Unknown" )
			entry.Header = GetPackageTitle( package );
		if ( string.IsNullOrWhiteSpace( entry.Description ) )
			entry.Description = package.Summary ?? "";
	}

	private void SetFilteredEntriesEnabled( bool enabled )
	{
		if ( _catalog is null )
			return;

		foreach ( var entry in GetFilteredEntries() )
		{
			entry.PolicyMode = PropCatalogPolicyMode.Manual;
			entry.Enabled = enabled;
			entry.VisibleInMenu = enabled;
			entry.Spawnable = enabled;
		}

		SaveCatalog();
	}

	private void SelectTab( bool enabledOnly )
	{
		_showEnabledOnly = enabledOnly;
		UpdateTabButtons();
		RebuildRows();
	}

	private void UpdateTabButtons()
	{
		if ( _allTabButton is null || _enabledTabButton is null )
			return;

		var total = _catalog?.Entries?.Count ?? 0;
		var enabled = _catalog?.Entries?.Count( entry => entry?.Enabled == true ) ?? 0;
		_allTabButton.Text = $"All props ({total})";
		_enabledTabButton.Text = $"Enabled ({enabled})";
		_allTabButton.Enabled = _showEnabledOnly;
		_enabledTabButton.Enabled = !_showEnabledOnly;
	}

	private IEnumerable<PropCatalogEntry> GetFilteredEntries()
	{
		if ( _catalog?.Entries is null )
			return Enumerable.Empty<PropCatalogEntry>();

		IEnumerable<PropCatalogEntry> entries = _catalog.Entries.Where( entry => entry is not null );
		if ( _showEnabledOnly )
			entries = entries.Where( entry => entry.Enabled );

		var search = (_search?.Value ?? "").Trim();
		if ( string.IsNullOrWhiteSpace( search ) )
			return entries;

		return entries.Where( entry =>
			$"{entry.Header} {entry.PackageIdent} {entry.Category} {entry.FilterReason}"
				.Contains( search, StringComparison.OrdinalIgnoreCase ) );
	}

	private void RebuildRows()
	{
		if ( _scroll is null || !_scroll.Canvas.IsValid() )
			return;

		_scroll.Canvas.Layout.Clear( true );
		if ( _catalog is null )
		{
			_count.Text = "0 props";
			return;
		}

		var filtered = GetFilteredEntries().ToList();
		var tabTotal = _showEnabledOnly
			? _catalog.Entries.Count( entry => entry?.Enabled == true )
			: _catalog.Entries.Count;
		_count.Text = $"{filtered.Count} shown / {tabTotal} in tab";
		UpdateTabButtons();

		foreach ( var entry in filtered.Take( MaximumRenderedRows ) )
			_scroll.Canvas.Layout.Add( CreateEntryRow( entry ) );

		if ( filtered.Count > MaximumRenderedRows )
		{
			var hint = new Label( $"Only the first {MaximumRenderedRows} rows are rendered. Use search to narrow the list." );
			hint.WordWrap = true;
			_scroll.Canvas.Layout.Add( hint );
		}

		_scroll.Canvas.Layout.AddStretchCell();
	}

	private Widget CreateEntryRow( PropCatalogEntry entry )
	{
		var row = new Widget( _scroll.Canvas );
		row.FixedHeight = 78;
		row.Layout = Layout.Row();
		row.Layout.Margin = 6;
		row.Layout.Spacing = 8;
		row.SetStyles( "background-color: #25282d; border-radius: 4px;" );

		var thumbnail = row.Layout.Add( new Widget( row ) );
		thumbnail.FixedSize = 62;
		thumbnail.OnPaintOverride = () =>
		{
			var rect = new Rect( 0, thumbnail.Size );
			if ( !string.IsNullOrWhiteSpace( entry.ThumbnailUrl ) )
				Paint.Draw( rect, entry.ThumbnailUrl, borderRadius: 4 );
			else
			{
				Paint.SetBrushAndPen( Theme.ControlBackground );
				Paint.DrawRect( rect, 4 );
			}
			return true;
		};

		var details = row.Layout.AddColumn( 1 );
		var title = details.Add( new Label( entry.Header ?? "Unknown" ) );
		title.ToolTip = entry.Description;
		details.Add( new Label( entry.PackageIdent ?? "" ) );
		details.Add( new Label( $"{entry.Category} | {entry.FilterReason}" ) );

		var enabled = row.Layout.Add( new Checkbox( "Enabled", row ) { Value = entry.Enabled } );
		enabled.Toggled += () =>
		{
			entry.PolicyMode = PropCatalogPolicyMode.Manual;
			entry.Enabled = enabled.Value;
			if ( !entry.Enabled )
			{
				entry.VisibleInMenu = false;
				entry.Spawnable = false;
			}
			SaveCatalog();
		};

		var visible = row.Layout.Add( new Checkbox( "In menu", row ) { Value = entry.VisibleInMenu } );
		visible.Toggled += () =>
		{
			entry.PolicyMode = PropCatalogPolicyMode.Manual;
			entry.VisibleInMenu = visible.Value;
			if ( visible.Value ) entry.Enabled = true;
			SaveCatalog();
		};

		var spawnable = row.Layout.Add( new Checkbox( "Spawnable", row ) { Value = entry.Spawnable } );
		spawnable.Toggled += () =>
		{
			entry.PolicyMode = PropCatalogPolicyMode.Manual;
			entry.Spawnable = spawnable.Value;
			if ( spawnable.Value ) entry.Enabled = true;
			SaveCatalog();
		};

		var automatic = row.Layout.Add( new Button( "Auto", "autorenew", row ) );
		automatic.ToolTip = "Return this prop to automatic allowlist/blacklist rules";
		automatic.Clicked = () =>
		{
			entry.PolicyMode = PropCatalogPolicyMode.Automatic;
			_catalog.ApplyFilters();
			SaveCatalog();
		};

		return row;
	}

	private void SaveCatalog( bool rebuild = true )
	{
		if ( _catalogAsset is null || _catalog is null )
			return;

		_catalog.StateHasChanged();
		var saved = _catalogAsset.SaveToDisk( _catalog );
		_status.Text = saved ? "Catalog saved." : "Failed to save catalog.";
		if ( rebuild ) RebuildRows();
	}

	private PropCatalogEntry FindByPackageIdent( string ident )
	{
		return _catalog?.Entries?.FirstOrDefault( entry => entry is not null &&
			(string.Equals( entry.PackageIdent, ident, StringComparison.OrdinalIgnoreCase )
			|| string.Equals( entry.Id, ident, StringComparison.OrdinalIgnoreCase )) );
	}

	private static bool IsUsableModelPackage( Package package )
	{
		return package is not null
			&& package.Public
			&& !package.Archived
			&& !string.IsNullOrWhiteSpace( GetPackageIdent( package ) )
			&& (string.Equals( package.TypeName, "model", StringComparison.OrdinalIgnoreCase )
				|| GetPrimaryAsset( package ).EndsWith( ".vmdl", StringComparison.OrdinalIgnoreCase ));
	}

	private static string GetPrimaryAsset( Package package )
	{
		if ( package is null )
			return "";

		return string.IsNullOrWhiteSpace( package.PrimaryAsset )
			? package.GetMeta( "PrimaryAsset", "" )
			: package.PrimaryAsset;
	}

	private static string GetPackageIdent( Package package )
	{
		return (package?.FullIdent ?? package?.Ident ?? "").Trim();
	}

	private static string GetPackageTitle( Package package )
	{
		var title = (package?.Title ?? "").Trim();
		return string.IsNullOrWhiteSpace( title ) ? GetPackageIdent( package ) : title;
	}
}
