using Sandbox;
using Sandbox.Diagnostics;
using System.Collections.Generic;
using System.Linq;

public sealed partial class PlayerQuest
{
	private static readonly Logger Logger = new( "PlayerQuest" );

	private bool loadedFromDisk = false;

	partial void OnStartHost()
	{
		if ( !Networking.IsHost ) return;

		if ( EnsureLoadedFromDisk() )
			MarkIntroQuest.EnsureStarterQuest( this );
		OnHostStateChangedServer();
	}

	partial void OnDestroyHost()
	{
		if ( !Networking.IsHost ) return;

		SaveToDisk();
	}

	/// <summary>Вызывается на хосте после любой мутации. Синхронизация владельцу и сохранение на диск.</summary>
	partial void OnHostStateChangedServer()
	{
		if ( !Networking.IsHost ) return;
		SyncToOwner();
		SaveToDisk();
	}

	public bool EnsureLoadedFromDisk()
	{
		if ( !Networking.IsHost ) return false;
		if ( loadedFromDisk ) return true;

		return LoadFromDisk();
	}

	private void SyncToOwner()
	{
		var current = CurrentQuests.Select( q => q.ToSnapshot() ).ToArray();
		var finished = FinishedQuests.Select( q => q.ToSnapshot() ).ToArray();

		var owner = Network.Owner;
		if ( owner == null || owner.IsHost )
		{
			// Listen server: владелец и хост — одно лицо, применяем локально без RPC.
			ApplyClientSnapshot( current, finished );
			return;
		}

		RpcSyncQuests( current, finished );
	}

	private string SavePath
	{
		get
		{
			var sid = Network.Owner?.SteamId ?? 0;
			return GetSavePath( sid );
		}
	}

	private static string GetSavePath( long steamId ) => $"quests_{steamId}.json";

	private class SaveData
	{
		public List<QuestSnapshot> Current { get; set; } = new();
		public List<QuestSnapshot> Finished { get; set; } = new();
	}

	/// <summary>
	/// Clears only Mark's tutorial progress. Online players are restarted and synced immediately;
	/// offline players receive the starter quest the next time their PlayerQuest component loads.
	/// </summary>
	public static bool HostResetMarkTutorial( long steamId, out string error )
	{
		error = null;

		if ( !Networking.IsHost )
		{
			error = "The command must run on the host.";
			return false;
		}

		if ( steamId <= 0 )
		{
			error = "Invalid SteamId.";
			return false;
		}

		try
		{
			var online = Game.ActiveScene?.GetAllComponents<PlayerQuest>()
				.FirstOrDefault( quest => quest.IsValid() && quest.GameObject.Network.Owner?.SteamId.Value == steamId );

			if ( online.IsValid() )
			{
				online.EnsureLoadedFromDisk();
				online.CurrentQuests.RemoveAll( IsMarkTutorial );
				online.FinishedQuests.RemoveAll( IsMarkTutorial );

				if ( !MarkIntroQuest.EnsureStarterQuest( online ) )
				{
					online.OnHostStateChanged();
					error = "Mark's tutorial resources are not ready.";
					return false;
				}

				MarkIntroQuest.ApplyInventoryUnlocks( online );
				online.OnHostStateChanged();
				return true;
			}

			var path = GetSavePath( steamId );
			var data = FileSystem.Data.FileExists( path )
				? Json.Deserialize<SaveData>( FileSystem.Data.ReadAllText( path ) ) ?? new SaveData()
				: new SaveData();

			data.Current ??= new();
			data.Finished ??= new();
			data.Current.RemoveAll( snapshot => IsMarkTutorial( snapshot ) );
			data.Finished.RemoveAll( snapshot => IsMarkTutorial( snapshot ) );
			FileSystem.Data.WriteAllText( path, Json.Serialize( data ) );
			return true;
		}
		catch ( System.Exception ex )
		{
			Logger.Warning( $"HostResetMarkTutorial failed for {steamId}: {ex.Message}" );
			error = ex.Message;
			return false;
		}
	}

	private static bool IsMarkTutorial( Quest quest )
		=> string.Equals( quest?.QuestDefinition?.Id, MarkIntroQuest.QuestId, System.StringComparison.OrdinalIgnoreCase );

	private static bool IsMarkTutorial( QuestSnapshot snapshot )
		=> string.Equals( snapshot.QuestId, MarkIntroQuest.QuestId, System.StringComparison.OrdinalIgnoreCase );

	private void SaveToDisk()
	{
		if ( !Networking.IsHost ) return;
		var owner = Network.Owner;
		if ( owner == null || owner.SteamId == 0 ) return;

		try
		{
			var data = new SaveData
			{
				Current = CurrentQuests.Select( q => q.ToSnapshot() ).ToList(),
				Finished = FinishedQuests.Select( q => q.ToSnapshot() ).ToList(),
			};
			var json = Json.Serialize( data );
			FileSystem.Data.WriteAllText( SavePath, json );
		}
		catch ( System.Exception ex )
		{
			Logger.Warning( $"SaveToDisk failed: {ex.Message}" );
		}
	}

	private bool LoadFromDisk()
	{
		if ( !Networking.IsHost ) return false;
		if ( loadedFromDisk ) return true;

		var owner = Network.Owner;
		if ( owner == null || owner.SteamId == 0 ) return false;

		loadedFromDisk = true;

		try
		{
			if ( !FileSystem.Data.FileExists( SavePath ) ) return true;
			var json = FileSystem.Data.ReadAllText( SavePath );
			var data = Json.Deserialize<SaveData>( json );
			if ( data == null ) return true;

			CurrentQuests = data.Current
				.Select( Quest.FromSnapshot )
				.Where( q => q != null )
				.ToList();

			FinishedQuests = data.Finished
				.Select( s =>
				{
					var q = Quest.FromSnapshot( s );
					if ( q != null ) q.IsFinish = true;
					return q;
				} )
				.Where( q => q != null )
				.ToList();
		}
		catch ( System.Exception ex )
		{
			Logger.Warning( $"LoadFromDisk failed: {ex.Message}" );
		}

		return true;
	}
}
