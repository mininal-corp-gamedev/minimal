using Sandbox;

public sealed class MoneyDropped : Component, Component.IPressable
{
    [Property, Sync(SyncFlags.FromHost)] public int Money { get; set; } = 0;

    public bool Press( IPressable.Event e )
    {
        var source = e.Source?.GameObject;
        if ( !source.IsValid() )
            return false;

        if ( !source.Components.TryGet<Player>( out var ply, FindMode.EverythingInSelfAndParent ) )
            return false;

        if ( ply.IsProxy )
            return false;

        RpcTakeDroppedMoney( GameObject );
        return true;
    }

    [Rpc.Host]
    private void RpcTakeDroppedMoney( GameObject moneyGo )
    {
        if ( !Networking.IsHost ) return;

        var caller = Rpc.Caller;
        if ( caller is null ) return;

        var dropped = moneyGo.Components.Get<MoneyDropped>();
        if ( !dropped.IsValid() || dropped.Money <= 0 )
            return;

        var player = FindPlayerBySteamId( caller.SteamId.Value );
        if ( !player.IsValid() )
            return;

        if ( Vector3.DistanceBetween( player.WorldPosition, dropped.WorldPosition ) > 120f )
            return;

        var amount = dropped.Money;
        dropped.Money = 0;
        player.Money += amount;

        using ( Rpc.FilterInclude( c => c.SteamId.Value == caller.SteamId.Value ) )
        {
            RpcNotifyTakeDroppedMoney( amount );
        }

        dropped.GameObject.Destroy();
    }

    [Rpc.Broadcast]
    private void RpcNotifyTakeDroppedMoney( int amount )
    {
        Notification.Info( $"Ты поднял ${amount}.", 3.5f );
    }

    private static Player FindPlayerBySteamId( long steamId )
    {
        var scene = Game.ActiveScene;
        if ( scene is null )
            return null;

        foreach ( var player in scene.GetAllComponents<Player>() )
        {
            if ( player.GameObject.Network.Owner?.SteamId.Value == steamId )
                return player;
        }

        return null;
    }
}
