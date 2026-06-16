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

		LoadFromDisk();
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
			return $"quests_{sid}.json";
		}
	}

	private class SaveData
	{
		public List<QuestSnapshot> Current { get; set; } = new();
		public List<QuestSnapshot> Finished { get; set; } = new();
	}

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

	private void LoadFromDisk()
	{
		if ( !Networking.IsHost ) return;
		if ( loadedFromDisk ) return;
		loadedFromDisk = true;

		var owner = Network.Owner;
		if ( owner == null || owner.SteamId == 0 ) return;

		try
		{
			if ( !FileSystem.Data.FileExists( SavePath ) ) return;
			var json = FileSystem.Data.ReadAllText( SavePath );
			var data = Json.Deserialize<SaveData>( json );
			if ( data == null ) return;

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
	}
}
