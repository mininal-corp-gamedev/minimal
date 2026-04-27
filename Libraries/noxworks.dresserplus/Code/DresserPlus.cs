using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Sandbox;

/// <summary>
/// Dresses a Citizen or Human using one of four clothing sources (Manual, LocalUser, OwnerUser, Hybrid).
/// Body attributes (height, age, tint) are synced across the network in real time.
/// </summary>
[Title( "Dresser Plus" )]
[Category( "Game" )]
[Icon( "checkroom" )]
public sealed class DresserPlus : Component, Component.ExecuteInEditor
{
	public enum ClothingSource
	{
		/// <summary>
		/// Dresses using manually selected clothing.
		/// </summary>
		[Icon( "format_list_bulleted" )]
		Manual,

		/// <summary>
		/// Dresses using the local client's avatar.
		/// Each client sees their own clothing.
		/// </summary>
		[Icon( "sync_disabled" )]
		LocalUser,

		/// <summary>
		/// Dresses using the avatar of this GameObject's network owner.
		/// All clients see that same clothing.
		/// </summary>
		[Icon( "sync" )]
		OwnerUser,

		/// <summary>
		/// Dresses using the avatar of this GameObject's network owner with selected categories stripped and manual clothing applied on top.
		/// All clients see that same clothing.
		/// </summary>
		[Icon( "cloud_sync" )]
		Hybrid
	}

	/// <summary>
	/// Who we are dressing.
	/// This should be the renderer of the body of a Citizen or Human.
	/// </summary>
	[Property]
	public SkinnedModelRenderer BodyTarget { get; set; }

	/// <summary>
	/// Where the clothing should be sourced from.
	/// </summary>
	[Property]
	public ClothingSource Source { get; set; }
	
	/// <summary>
	/// Strips any clothing items that are not owned in the client's Steam Inventory.
	/// Disable only if your game handles ownership checks itself.
	/// </summary>
	[Property, Group( "Parameters" )]
	[ShowIf( nameof(ShowOwnerOptions), true )]
	public bool RemoveUnownedItems { get; set; } = true;

	/// <summary>
	/// Whether the height should be scaled or a standard value for all bodies be used.
	/// When false, changing the height scale won't have any effect.
	/// </summary>
	[Property, Group( "Parameters" )]
	public bool ApplyHeightScale { get; set; } = true;

	/// <summary>
	/// Controls the body's height scale.
	/// </summary>
	[Property, Group( "Parameters" ), Range( 0.8f, 1.2f )]
	[ShowIf( nameof(ShowHeightOption), true )]
	[Change( nameof(OnManualChange) )]
	[Sync]
	public float ManualHeight { get; set; } = 1.0f;

	/// <summary>
	/// Controls the body's intensity of skin aging.
	/// 0 is smooth/young, 1 is wrinkled/aged.
	/// </summary>
	[Property, Group( "Parameters" ), Range( 0, 1 )]
	[ShowIf( nameof(ShowManualOptions), true )]
	[Change( nameof(OnManualChange) )]
	[Sync]
	public float ManualAge { get; set; } = 0.5f;

	/// <summary>
	/// Blends the body's skin color along the skin color spectrum.
	/// </summary>
	[Property, Group( "Parameters" ), Range( 0, 1 )]
	[ShowIf( nameof(ShowManualOptions), true )]
	[Change( nameof(OnManualChange) )]
	[Sync]
	public float ManualTint { get; set; } = 0.5f;

	/// <summary>
	/// Clothing items to dress the body with.
	/// </summary>
	[Property, Group( "Parameters" )]
	[ShowIf( nameof(ShowManualOptions), true )]
	public List<ClothingContainer.ClothingEntry> Clothing { get; set; }

	/// <summary>
	/// Workshop clothing package identifiers to fetch and dress the body with.
	/// </summary>
	[Property, Group( "Parameters" )]
	[ShowIf( nameof(ShowManualOptions), true )]
	public List<string> WorkshopClothing { get; set; }

	/// <summary>
	/// Clothing categories to strip from the owner's avatar before applying manual clothing.
	/// </summary>
	[Property, Group( "Parameters" )]
	[ShowIf( nameof( Source ), ClothingSource.Hybrid )]
	public List<Clothing.ClothingCategory> StrippedCategories { get; set; }

	/// <summary>
	/// True while <see cref="Apply()"/> is running asynchronously.
	/// </summary>
	public bool IsDressing { get; private set; }

	private bool ShowManualOptions => Source is ClothingSource.Manual or ClothingSource.Hybrid;
	private bool ShowOwnerOptions => Source is ClothingSource.OwnerUser or ClothingSource.Hybrid;
	private bool ShowHeightOption => ShowManualOptions && ApplyHeightScale;
	private bool NeedsNetworkOwner => Source is ClothingSource.OwnerUser or ClothingSource.Hybrid;

	private CancellationTokenSource _cts;

	protected override void OnAwake()
	{
		if ( IsProxy ) return;

		if ( !BodyTarget.IsValid() )
			BodyTarget = GetComponentInChildren<SkinnedModelRenderer>();

		_ = ApplyWhenReady();
	}

	protected override void OnEnabled()
	{
		if ( IsProxy )
			ApplyAttributes();
	}

	protected override void OnValidate()
	{
		if ( IsProxy ) return;

		base.OnValidate();

		using var p = Scene.Push();

		if ( !BodyTarget.IsValid() )
			BodyTarget = GetComponentInChildren<SkinnedModelRenderer>();

		_ = Apply();
	}

	protected override void OnDestroy()
	{
		CancelDressing();
	}

	/// <summary>
	/// Fetches a workshop clothing package by identifier, mounts it, and returns the clothing resource.
	/// </summary>
	/// <param name="ident">The workshop package identifier.</param>
	/// <param name="token">The async operation cancellation token.</param>
	/// <returns>The clothing resource, or null if the package was not found or invalid.</returns>
	private static async Task<Clothing> InstallWorkshopClothing( string ident, CancellationToken token )
	{
		if ( string.IsNullOrEmpty( ident ) ) return null;

		var package = await Package.FetchAsync( ident, false );
		if ( package is null )
		{
			Log.Warning( $"DresserPlus: Failed to fetch workshop package '{ident}'" );
			return null;
		}
		if ( package.TypeName != "clothing" ) return null;
		if ( token.IsCancellationRequested ) return null;

		var primaryAsset = package.PrimaryAsset;
		if ( string.IsNullOrWhiteSpace( primaryAsset ) ) return null;

		var fs = await package.MountAsync();
		if ( fs is null )
		{
			Log.Warning( $"DresserPlus: Failed to mount workshop package '{ident}'" );
			return null;
		}
		
		return token.IsCancellationRequested ? null : ResourceLibrary.Get<Clothing>( primaryAsset );
	}

	/// <summary>
	/// Builds a <see cref="ClothingContainer"/> based on the selected <see cref="Source"/>.
	/// </summary>
	/// <param name="token">The async operation cancellation token.</param>
	/// <returns>The built clothing container, or null if the source is not recognized.</returns>
	private async ValueTask<ClothingContainer> GetClothing( CancellationToken token )
	{
		switch ( Source )
		{
			case ClothingSource.Manual:
				{
					var clothing = new ClothingContainer();
					await AddManualClothing( clothing, token );
					clothing.Normalize();
					return clothing;
				}
			case ClothingSource.LocalUser:
				return ClothingContainer.CreateFromLocalUser();
			case ClothingSource.OwnerUser when Network.Owner != null:
				return ClothingContainer.CreateFromConnection( Network.Owner, RemoveUnownedItems );
			case ClothingSource.OwnerUser:
				return new ClothingContainer();
			case ClothingSource.Hybrid:
				{
					var clothing = Network.Owner != null ? ClothingContainer.CreateFromConnection( Network.Owner, RemoveUnownedItems ) : new ClothingContainer();

					if ( StrippedCategories is { Count: > 0 } )
						clothing.Clothing.RemoveAll( e => StrippedCategories.Contains( e.Clothing.Category ) );

					await AddManualClothing( clothing, token );
					clothing.Normalize();
					return clothing;
				}
			default:
				return null;
		}
	}

	/// <summary>
	/// Adds <see cref="Clothing"/>, <see cref="WorkshopClothing"/>, and body attributes
	/// (<see cref="ManualHeight"/>, <see cref="ManualAge"/>, <see cref="ManualTint"/>) to the given <see cref="ClothingContainer"/>.
	/// </summary>
	/// <param name="clothing">The container to add items and attributes to.</param>
	/// <param name="token">The async operation cancellation token.</param>
	private async ValueTask AddManualClothing( ClothingContainer clothing, CancellationToken token )
	{
		clothing.AddRange( Clothing );
		clothing.Height = ManualHeight.Remap( 0.8f, 1.2f, 0, 1, true );
		clothing.Age = ManualAge;
		clothing.Tint = ManualTint;

		if ( WorkshopClothing is not { Count: > 0 } ) return;

		var tasks = WorkshopClothing.Select( s => InstallWorkshopClothing( s, token ) ).ToArray();
		await Task.WhenAll( tasks );

		foreach ( var task in tasks )
		{
			if ( task.Result is not null )
				clothing.Add( task.Result );
		}
	}

	/// <summary>
	/// Called when <see cref="ManualHeight"/>, <see cref="ManualAge"/>, or <see cref="ManualTint"/> is changed.
	/// </summary>
	private void OnManualChange()
	{
		ApplyAttributes();
	}
	
	/// <summary>
	/// Waits for the network owner to be assigned (if required by the current <see cref="Source"/>), then applies clothing.
	/// </summary>
	private async ValueTask ApplyWhenReady()
	{
		if ( NeedsNetworkOwner )
		{
			while ( Network.Owner is null )
			{
				if ( !this.IsValid() ) return;
				await Task.Frame();
			}
		}

		await Apply();
	}

	/// <summary>
	/// Applies <see cref="ManualHeight"/>, <see cref="ManualAge"/>, and <see cref="ManualTint"/> on the <see cref="BodyTarget"/>.
	/// </summary>
	private void ApplyAttributes()
	{
		if ( BodyTarget is null ) return;

		if ( ApplyHeightScale )
			BodyTarget.Set( "scale_height", ManualHeight );
		else
			BodyTarget.Set( "scale_height", 1 );

		foreach ( var r in BodyTarget.GetComponentsInChildren<SkinnedModelRenderer>() )
		{
			r.Attributes.Set( "skin_age", ManualAge );
			r.Attributes.Set( "skin_tint", ManualTint );
		}
	}

	/// <summary>
	/// Button wrapper for <see cref="Apply()"/>.
	/// </summary>
	[Button( "Apply Changes" )]
	private void ApplyButton() => _ = Apply();

	/// <summary>
	/// Dresses the <see cref="BodyTarget"/> using the selected <see cref="Source"/>.
	/// Cancels any in-progress dressing, fetches the clothing, and applies it asynchronously.
	/// </summary>
	public async ValueTask Apply()
	{
		CancelDressing();

		if ( !BodyTarget.IsValid() ) return;

		_cts = new CancellationTokenSource();
		var token = _cts.Token;

		IsDressing = true;

		try
		{
			var clothing = await GetClothing( token );
			if ( clothing is null || token.IsCancellationRequested ) return;
			if ( !BodyTarget.IsValid() ) return;

			if ( !ApplyHeightScale )
				clothing.Height = 1;

			clothing.Normalize();

			await clothing.ApplyAsync( BodyTarget, token );

			ManualHeight = clothing.Height.Remap( 0, 1, 0.8f, 1.2f, true );
			ManualTint = clothing.Tint;
			ManualAge = clothing.Age;

			ApplyAttributes();
		}
		finally
		{
			IsDressing = false;
		}
	}

	/// <summary>
	/// Sets <see cref="Clothing"/> to the given items and applies them to the <see cref="BodyTarget"/>.
	/// </summary>
	/// <param name="clothing">The clothing items to apply.</param>
	public ValueTask Apply( List<ClothingContainer.ClothingEntry> clothing )
	{
		Clothing = clothing;
		return Apply();
	}

	/// <summary>
	/// Removes all clothing from the <see cref="BodyTarget"/>, stripping it back to the bare model.
	/// </summary>
	[Button( "Clear Changes" )]
	public void Clear()
	{
		CancelDressing();

		if ( !BodyTarget.IsValid() ) return;

		var clothing = new ClothingContainer();
		clothing.Apply( BodyTarget );

		ManualHeight = 1.0f;
		ManualAge = 0.5f;
		ManualTint = 0.5f;
		ApplyAttributes();
	}
	
	/// <summary>
	/// Cancels any in-progress async dressing and disposes the cancellation token.
	/// </summary>
	public void CancelDressing()
	{
		_cts?.Cancel();
		_cts?.Dispose();
		_cts = null;
	}
}
