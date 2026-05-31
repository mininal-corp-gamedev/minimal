using Sandbox;

public sealed class NpcBuyerCactus : Component, Component.IPressable
{
    public bool Press(IPressable.Event e)
    {
        if (IsProxy) return false;

        var go = e.Source.GameObject;

        if (!go.Components.TryGet<Player>(out var ply, FindMode.EverythingInSelfAndParent)) return false;

        if ( Networking.IsHost )
        {
#if SERVER
            SellCactus( ply );
#endif
            return true;
        }

        RpcSellCactus();
        return true;
    }

    [Rpc.Host]
    private void RpcSellCactus()
    {
#if SERVER
        if ( !Networking.IsHost ) return;

        var caller = Rpc.Caller;
        if ( caller is null ) return;

        var ply = Player.FindPlayerBySteamId( caller.SteamId.Value );
        if ( !ply.IsValid() || ply.GameObject.Network.Owner != caller )
            return;

        SellCactus( ply );
#endif
    }

#if SERVER
    private static void SellCactus( Player ply )
    {
        if ( !ply.IsValid() ) return;

        var cactuses = ply.CactusCount;
        if (cactuses <= 0) return;

        ply.Money += 20 * cactuses;
        ply.CactusCount = 0;
    }
#endif

    protected override void OnUpdate()
	{

	}
}
