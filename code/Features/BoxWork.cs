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
        // TODO (дедик): заменить Networking.IsHost на #if SERVER
        if ( !Networking.IsHost ) return;

        CanLoot = true;
        _delayToRefresh = 0;

        RpcRefreshVisual();
    }

    // ─────────────────────────────────────────────
    //  RpcTakeBox — авторитетная обработка на хосте
    //  Принимает GameObject а не Component — RPC так надёжнее
    // ─────────────────────────────────────────────

    [Rpc.Host]
    public void RpcTakeBox( GameObject boxGo )
    {
        // TODO (дедик): заменить Networking.IsHost на #if SERVER
        if ( !Networking.IsHost ) return;

        // 1. Получаем компонент коробки
        var box = boxGo.Components.Get<BoxWork>();

        if ( box is null )
        {
            Log.Warning( "RpcTakeBox: BoxWork component not found" );
            return;
        }

        // 2. Ищем Player по SteamId звонящего
        Player ply = null;

        foreach ( var go in Scene.GetAllObjects( true ) )
        {
            if ( !go.Components.TryGet<Player>( out var candidate ) ) continue;

            if ( candidate.GameObject.Network.Owner.SteamId == Rpc.Caller.SteamId )
            {
                ply = candidate;
                break;
            }
        }

        if ( ply is null )
        {
            Log.Warning( $"RpcTakeBox: player not found for {Rpc.Caller.DisplayName}" );
            return;
        }

        // 3. Проверка дистанции
        var dist = Vector3.DistanceBetween( ply.WorldPosition, box.WorldPosition );

        if ( dist > MaxDistance )
        {
            Log.Warning( $"RpcTakeBox: {Rpc.Caller.DisplayName} too far ({dist:F0} > {MaxDistance})" );
            return;
        }

        // 4. Проверка что коробка ещё доступна (race condition — два игрока жмут одновременно)
        if ( !box.CanLoot )
        {
            Log.Warning( "RpcTakeBox: box already looted" );
            return;
        }

        // 5. Засчитываем
        box.CanLoot = false;
        box._delayToRefresh = box.Delay;

        ply.TakeBox( box.Amount );

        box.RpcLootVisual();
        

        Log.Info( $"{Rpc.Caller.DisplayName} looted ${box.Amount} from box" );
    }

    // ─────────────────────────────────────────────
    //  Таймер восстановления
    // ─────────────────────────────────────────────

    protected override void OnFixedUpdate()
    {
        // TODO (дедик): заменить Networking.IsHost на #if SERVER
        if ( !Networking.IsHost ) return;

        if ( CanLoot ) return;

        if ( _delayToRefresh )
        {
            Refresh();
        }
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