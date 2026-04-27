using Sandbox;

public sealed class MoneyDropped : Component
{
    [Property, Sync(SyncFlags.FromHost)] public int Money { get; set; } = 0;

    protected override void OnUpdate()
	{

	}
}
