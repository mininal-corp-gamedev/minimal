using Sandbox;

public sealed class Cactus : Component, Component.IPressable
{
    [Sync] public bool CanHarvest { get; set; } = true;
    [Property] public int Count { get; set; } = 1;
    [Property] public float Delay { get; set; } = 10f;
    [Property] public ModelRenderer Renderer { get; set; }

    private TimeUntil _delayToRefresh = 0;

    public void Refresh()
    {
        Renderer.Tint = Color.Green; //todo everyone rpc

        CanHarvest = true;
    }

    public void Harvest(Player ply)
    {
        Renderer.Tint = Color.Black;

        CanHarvest = false;

        ply.CactusCount += Count;

        Notification.Info($"You harvest {Count} cactus", 3.5f);

        Log.Info($"{ply} harvest {Count} cactus");
    }

    protected override void OnFixedUpdate()
	{
        if (CanHarvest) return;

        if (_delayToRefresh)
        {
            _delayToRefresh = Delay;

            Refresh();
        }
	}

    public bool Press(IPressable.Event e)
    {
        var go = e.Source.GameObject;

        if (!go.Components.TryGet<Player>(out var ply, FindMode.EverythingInSelfAndParent)) return false;

        Harvest(ply);

        return true;
    }
}
