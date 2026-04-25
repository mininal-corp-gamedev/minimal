using Sandbox;

/// <summary>
/// Куб на карте. IPressable открывает UI казино у нажавшего игрока.
/// Вся логика ставки — на хосте.
/// </summary>
public sealed class ColorSlot : Component, Component.IPressable
{
    [Property, Group( "Bet" )] public int BetAmount { get; set; } = 50;

    // ── Результат последнего броска (синкается всем, нужен панели) ──
    [Sync( SyncFlags.FromHost )] public bool LastResultGreen { get; private set; }
    [Sync( SyncFlags.FromHost )] public bool HasResult       { get; private set; }

    // ─────────────────────────────────────────────
    //  IPressable
    // ─────────────────────────────────────────────

    public bool Press( IPressable.Event e )
    {
        var go = e.Source.GameObject;

        if ( !go.Components.TryGet<Player>( out var ply, FindMode.EverythingInSelfAndParent ) )
            return false;

        if ( ply.IsProxy ) return false;

        // Открываем UI прямо на клиенте — ищем ColorSlotScreen на Screen-объекте игрока
        var screen = ply.Components.Get<ColorSlotScreen>( FindMode.EverythingInSelfAndDescendants );
        screen?.Open( this );

        return true;
    }

    // ─────────────────────────────────────────────
    //  RPC Host — авторитетная обработка ставки
    // ─────────────────────────────────────────────

    [Rpc.Host]
    public void RpcPlaceBet( GameObject playerGo, bool betOnGreen )
    {
        if ( !Networking.IsHost ) return;

        if ( !playerGo.Components.TryGet<Player>( out var ply ) )
        {
            Log.Warning( "ColorSlot: Player not found" );
            return;
        }

        // Сверяем владельца
        if ( ply.GameObject.Network.Owner.SteamId != Rpc.Caller.SteamId )
        {
            Log.Warning( $"ColorSlot: SteamId mismatch! Caller={Rpc.Caller.SteamId}" );
            return;
        }

        // Достаточно денег?
        if ( ply.Money < BetAmount )
        {
            Log.Info( $"ColorSlot: {Rpc.Caller.DisplayName} not enough money" );
            RpcSendResult( playerGo, won: false, resultGreen: false, noMoney: true );
            return;
        }

        // Списываем ставку
        ply.Money -= BetAmount;

        // Бросок монеты
        bool resultGreen = Game.Random.Int( 0, 1 ) == 1;
        bool won         = resultGreen == betOnGreen;

        if ( won )
            ply.Money += BetAmount * 2;

        // Сохраняем для синка (World UI или другие зрители)
        LastResultGreen = resultGreen;
        HasResult       = true;

        Log.Info( $"ColorSlot: {Rpc.Caller.DisplayName} bet=${BetAmount} onGreen={betOnGreen} result={resultGreen} won={won}" );

        RpcSendResult( playerGo, won, resultGreen, noMoney: false );
    }

    // Доставляем результат только тому игроку, чей gameobject передали
    [Rpc.Owner]
    private void RpcSendResult( GameObject playerGo, bool won, bool resultGreen, bool noMoney )
    {
        var screen = playerGo.Components.Get<ColorSlotScreen>( FindMode.EverythingInSelfAndDescendants );
        screen?.OnResult( won, resultGreen, noMoney );
    }
}
