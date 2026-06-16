using Sandbox;
using System.Collections.Generic;
using System.Linq;

public sealed partial class PlayerQuest : Component
{
	public static PlayerQuest Local { get; private set; }

	/// <summary>Авторитативный список активных квестов. Достоверен только на хосте.</summary>
	public List<Quest> CurrentQuests { get; set; } = new();

	/// <summary>Авторитативный список завершённых квестов. Достоверен только на хосте.</summary>
	public List<Quest> FinishedQuests { get; set; } = new();

	/// <summary>Зеркало активных квестов на стороне владельца, для HUD.</summary>
	public List<Quest> ClientCurrentQuests { get; private set; } = new();

	/// <summary>Зеркало завершённых квестов на стороне владельца.</summary>
	public List<Quest> ClientFinishedQuests { get; private set; } = new();

	protected override void OnStart()
	{
		if ( !IsProxy )
			Local = this;

		OnStartHost();
	}

	protected override void OnDestroy()
	{
		OnDestroyHost();

		if ( Local == this )
			Local = null;
	}

	// Implemented in PlayerQuest.Server.cs — host-only LoadFromDisk + initial sync.
	partial void OnStartHost();

	// Implemented in PlayerQuest.Server.cs — host-only SaveToDisk on destroy.
	partial void OnDestroyHost();

	// Implemented in PlayerQuest.Server.cs — host-only post-mutation hook (sync owner + save).
	public void OnHostStateChanged() => OnHostStateChangedServer();
	partial void OnHostStateChangedServer();

	public bool HasActiveQuest( QuestDefinition def )
	{
		if ( def == null ) return false;
		return CurrentQuests.Any( q => q.QuestDefinition == def );
	}

	public bool HasFinishedQuest( QuestDefinition def )
	{
		if ( def == null ) return false;
		return FinishedQuests.Any( q => q.QuestDefinition == def );
	}

	public Quest GetActiveQuest( QuestDefinition def )
	{
		if ( def == null ) return null;
		return CurrentQuests.FirstOrDefault( q => q.QuestDefinition == def );
	}

	[Rpc.Owner]
	private void RpcSyncQuests( QuestSnapshot[] current, QuestSnapshot[] finished )
	{
		ApplyClientSnapshot( current, finished );
	}

	internal void ApplyClientSnapshot( QuestSnapshot[] current, QuestSnapshot[] finished )
	{
		ClientCurrentQuests = current?
			.Select( Quest.FromSnapshot )
			.Where( q => q != null )
			.ToList() ?? new();

		ClientFinishedQuests = finished?
			.Select( Quest.FromSnapshot )
			.Where( q => q != null )
			.ToList() ?? new();
	}
}
