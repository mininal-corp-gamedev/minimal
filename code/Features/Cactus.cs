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
#if SERVER
        Renderer.Tint = Color.Green; //todo everyone rpc

        CanHarvest = true;
#endif
    }

    public void Harvest(Player ply)
    {
#if SERVER
        Renderer.Tint = Color.Black;

        CanHarvest = false;

        ply.CactusCount += Count;

        Notification.Info(GameLocalization.Format( "notify.cactus.harvest", "You harvested {0} cactus.", Count ), 3.5f);

        Log.Info($"{ply} harvest {Count} cactus");
#endif
    }

    protected override void OnFixedUpdate()
	{
#if SERVER
        if (CanHarvest) return;

        if (_delayToRefresh)
        {
            _delayToRefresh = Delay;

            Refresh();
        }
#endif
	}

    public bool Press(IPressable.Event e)
    {
        var go = e.Source.GameObject;

        if (!go.Components.TryGet<Player>(out var ply, FindMode.EverythingInSelfAndParent)) return false;

        if ( Networking.IsHost )
        {
#if SERVER
            Harvest(ply);
#endif
        }
        else
        {
            RpcHarvestCactus( GameObject );
        }

        return true;
    }

    [Rpc.Host]
    private void RpcHarvestCactus( GameObject cactusGo )
    {
#if SERVER
        if ( !Networking.IsHost ) return;
        if ( cactusGo != GameObject ) return;

        var caller = Rpc.Caller;
        if ( caller is null ) return;

        var player = Player.FindPlayerBySteamId( caller.SteamId.Value );
        if ( !player.IsValid() || player.GameObject.Network.Owner != caller )
            return;

        if ( !CanHarvest )
            return;

        Harvest( player );
#endif
    }
}
