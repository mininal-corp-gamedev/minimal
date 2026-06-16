using Sandbox;

public sealed partial class QuestManager : Component
{
	public static QuestManager Instance { get; private set; }

	protected override void OnStart()
	{
		if ( Instance == null )
			Instance = this;

		OnStartHost();
	}

	protected override void OnDestroy()
	{
		if ( Instance == this )
			Instance = null;
	}

	// Implemented in QuestManager.Server.cs — host-only quest database / handler bootstrap.
	partial void OnStartHost();
}
