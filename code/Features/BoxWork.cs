using Sandbox;

public sealed class BoxWork : Component, Component.IPressable
{
    [Sync] public bool CanLoot { get; set; } = true;
    [Property] public int Amount { get; set; } = 5;
    [Property] public float Delay { get; set; } = 10f;
    [Property] public float MaxDistance { get; set; } = 100f;
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
#if SERVER
        if ( !Networking.IsHost ) return;

        CanLoot = true;
        _delayToRefresh = 0;

        RpcRefreshVisual();
#endif
    }

    // ─────────────────────────────────────────────
    //  RpcTakeBox — авторитетная обработка на хосте
    //  Принимает GameObject а не Component — RPC так надёжнее
    // ─────────────────────────────────────────────

    [Rpc.Host]
    public void RpcTakeBox( GameObject boxGo )
    {
#if SERVER
        if ( !Networking.IsHost ) return;

        var caller = Rpc.Caller;
        if ( caller is null ) return;

        if ( boxGo != GameObject )
            return;

        // 1. Получаем компонент коробки
        var box = boxGo.Components.Get<BoxWork>();

        if ( !box.IsValid() )
        {
            Log.Warning( "RpcTakeBox: BoxWork component not found" );
            return;
        }

        // 2. Ищем Player по SteamId звонящего
        Player ply = null;

        foreach ( var go in Scene.GetAllObjects( true ) )
        {
            if ( !go.Components.TryGet<Player>( out var candidate ) ) continue;

            if ( candidate.GameObject.Network.Owner?.SteamId == caller.SteamId )
            {
                ply = candidate;
                break;
            }
        }

        if ( !ply.IsValid() || ply.GameObject.Network.Owner != caller )
        {
            Log.Warning( $"RpcTakeBox: player not found for {caller.DisplayName}" );
            return;
        }

        // 3. Проверка дистанции
        var dist = Vector3.DistanceBetween( ply.WorldPosition, box.WorldPosition );

        if ( dist > box.MaxDistance )
        {
            Log.Warning( $"RpcTakeBox: {caller.DisplayName} too far ({dist:F0} > {box.MaxDistance})" );
            return;
        }

        // 4. Проверка что коробка ещё доступна (race condition — два игрока жмут одновременно)
        if ( !box.CanLoot )
        {
            Log.Warning( "RpcTakeBox: box already looted" );
            return;
        }

        if ( box.Amount <= 0 )
            return;

        // 5. Засчитываем
        box.CanLoot = false;
        box._delayToRefresh = box.Delay;

        ply.TakeBox( box.Amount );

        box.RpcLootVisual();
        

        Log.Info( $"{caller.DisplayName} looted ${box.Amount} from box" );
#endif
    }

    // ─────────────────────────────────────────────
    //  Таймер восстановления
    // ─────────────────────────────────────────────

    protected override void OnFixedUpdate()
    {
#if SERVER
        if ( !Networking.IsHost ) return;

        if ( CanLoot ) return;

        if ( _delayToRefresh )
        {
            Refresh();
        }
#endif
    }

    // ─────────────────────────────────────────────
    //  Press — клиентские проверки, потом уходим на хост
    // ─────────────────────────────────────────────

    public bool Press( IPressable.Event e )
    {
        Log.Info( "Press called" );

        if ( !CanLoot )
        {
            Log.Info( "Press: CanLoot = false, skip" );
            return false;
        }

        var go = e.Source.GameObject;

        if ( !go.Components.TryGet<Player>( out var ply, FindMode.EverythingInSelfAndParent ) )
        {
            Log.Info( "Press: Player not found on source" );
            return false;
        }

        if ( ply.IsProxy )
        {
            Log.Info( "Press: IsProxy, skip" );
            return false;
        }

        Log.Info( $"Press: sending RpcTakeBox from {ply}" );
        RpcTakeBox( GameObject );

        return true;
    }
}
