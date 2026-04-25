using Sandbox;

/// <summary>
/// Компонент куба на карте.
/// IPressable → открывает ColorSlotPanel через ColorSlotState.
/// Вся логика ставки — [Rpc.Host].
/// </summary>
public sealed class ColorSlot : Component, Component.IPressable
{
    [Property, Group( "Bet" )] public int BetAmount { get; set; } = 50;

    // ─────────────────────────────────────────────
    //  IPressable — клиент нажал E
    // ─────────────────────────────────────────────

    public bool Press( IPressable.Event e )
    {
        var go = e.Source.GameObject;

        if ( !go.Components.TryGet<Player>( out var ply, FindMode.EverythingInSelfAndParent ) )
            return false;

        if ( ply.IsProxy ) return false;

        ColorSlotState.Instance?.Open( this );
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
            Log.Warning( "ColorSlot RpcPlaceBet: Player not found" );
            return;
        }

        // Защита от подмены — сверяем SteamId
        if ( ply.GameObject.Network.Owner.SteamId != Rpc.Caller.SteamId )
        {
            Log.Warning( $"ColorSlot RpcPlaceBet: SteamId mismatch! Caller={Rpc.Caller.SteamId}" );
            return;
        }

        // Недостаточно денег
        if ( ply.Money < BetAmount )
        {
            Log.Info( $"ColorSlot: {Rpc.Caller.DisplayName} not enough money (has {ply.Money}, need {BetAmount})" );
            ply.RpcColorSlotResult( won: false, resultGreen: false, noMoney: true );
            return;
        }

        // Списываем ставку
        ply.Money -= BetAmount;

        // Бросок монеты (50/50)
        bool resultGreen = Game.Random.Int( 0, 1 ) == 1;
        bool won         = resultGreen == betOnGreen;

        if ( won )
            ply.Money += BetAmount * 2;

        Log.Info( $"ColorSlot: {Rpc.Caller.DisplayName} bet=${BetAmount} onGreen={betOnGreen} result={resultGreen} won={won}" );

        // Отправляем результат через Player — [Rpc.Owner] на Player
        // доставит именно владельцу этого игрока
        ply.RpcColorSlotResult( won, resultGreen, noMoney: false );
    }
}