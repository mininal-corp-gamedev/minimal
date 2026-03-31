using Sandbox;
public sealed class BoxWork : Component, Component.IPressable
{
    [Sync] public bool CanLoot { get; set; } = true;
    [Property] public int Amount { get; set; } = 5;
    [Property] public float Delay { get; set; } = 10f;
    [Property] public ModelRenderer Renderer { get; set; }

    private TimeUntil _delayToRefresh = 0;

    // ─────────────────────────────────────────────
    //  RPC визуал — рассылаем всем
    // ─────────────────────────────────────────────

    [Rpc.Broadcast]
    public void RpcRefreshVisual()
    {
        Renderer.Tint = Color.White;
    }

    [Rpc.Broadcast]
    public void RpcLootVisual()
    {
        Renderer.Tint = Color.Black;
    }

    // ─────────────────────────────────────────────
    //  Refresh — восстанавливает коробку
    // ─────────────────────────────────────────────

    public void Refresh()
    {
        CanLoot = true;         // #if SERVER — вернуть на дедике
        RpcRefreshVisual();
    }

    // ─────────────────────────────────────────────
    //  Loot — игрок подбирает деньги
    // ─────────────────────────────────────────────

    public void Loot( Player ply )
    {
        CanLoot = false;        // #if SERVER — вернуть на дедике
        ply.Money += Amount;    // #if SERVER — вернуть на дедике
        _delayToRefresh = Delay;// #if SERVER — вернуть на дедике

        Log.Info( $"{ply} looted ${Amount} from box" );

        RpcLootVisual();
        RpcNotifyLooter( ply.Network.Owner, Amount );
    }

    // ─────────────────────────────────────────────
    //  RPC уведомление — только владельцу
    // ─────────────────────────────────────────────

    [Rpc.Owner]
    private void RpcNotifyLooter( Connection owner, int amount )
    {
        Notification.Info( $"You looted ${amount}", 3.5f );
    }

    // ─────────────────────────────────────────────
    //  Таймер восстановления
    // ─────────────────────────────────────────────

    protected override void OnFixedUpdate()
    {
        // #if SERVER — вернуть на дедике
        if ( CanLoot ) return;

        if ( _delayToRefresh )
        {
            Refresh();
        }
    }

    // ─────────────────────────────────────────────
    //  Press — взаимодействие игрока с коробкой
    // ─────────────────────────────────────────────

    public bool Press( IPressable.Event e )
    {
        if ( !CanLoot ) return false;

        var go = e.Source.GameObject;

        if ( !go.Components.TryGet<Player>( out var ply, FindMode.EverythingInSelfAndParent ) ) return false;

        Loot( ply );    // #if SERVER — вернуть на дедике

        return true;
    }
}