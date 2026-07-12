using Sandbox;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Host-side entry point for synchronizing the prop catalog with sbox.game Cloud.
/// The implementation lives in the server-only partial file.
/// </summary>
public sealed partial class PropCatalogSynchronizer : Component
{
	public static PropCatalogSynchronizer Instance { get; private set; }

	[Property] public bool AutoSyncOnStart { get; set; } = true;
	[Property] public int MaximumPackages { get; set; } = 5000;

	public bool IsSynchronizing { get; private set; }
	public int LastDiscoveredCount { get; private set; }
	public string LastError { get; private set; } = "";

	protected override void OnAwake()
	{
		Instance = this;
	}

	protected override void OnStart()
	{
#if SERVER
		if ( Networking.IsHost && AutoSyncOnStart )
			StartCloudSync();
#endif
	}

	protected override void OnDestroy()
	{
		if ( Instance == this )
			Instance = null;
	}

	public void SynchronizeNow()
	{
#if SERVER
		StartCloudSync();
#endif
	}

	private PropCatalog FindCatalog()
	{
		return PropCatalog.GetCurrent();
	}

	private void SetSyncStarted()
	{
		IsSynchronizing = true;
		LastError = "";
	}

	private void SetSyncCompleted( int discoveredCount )
	{
		LastDiscoveredCount = discoveredCount;
		IsSynchronizing = false;
	}

	private void SetSyncFailed( string error )
	{
		LastError = error ?? "Unknown cloud synchronization error.";
		IsSynchronizing = false;
	}

	partial void StartCloudSync();
}

public sealed class PropCatalogCacheData
{
	public string SourceOrganization { get; set; } = "facepunch";
	public string LastSuccessfulSyncUtc { get; set; } = "";
	public List<PropCatalogEntry> Entries { get; set; } = new();
}
