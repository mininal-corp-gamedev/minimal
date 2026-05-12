using Sandbox;
using System;
using System.Collections.Generic;

public sealed class SlotMachine : Component, Component.IPressable
{
    private const float PressCooldownSeconds = 0.25f;

    [Property, Group( "Gameplay" )] public int BetCost { get; set; } = 50;
    [Property, Group( "Gameplay" )] public float WinChance { get; set; } = 0.45f;
    [Property, Group( "Gameplay" )] public int MaxMultiplier { get; set; } = 20;
    [Property, Group( "Gameplay" )] public float MaxDistance { get; set; } = 120f;
    [Property, Group( "Visual" )] public float SpinDuration { get; set; } = PressCooldownSeconds;

    public int LocalMultiplier => _localMultiplier;
    public int LocalPotentialPayout => _localMultiplier * BetCost;
    public string LocalStatusText => _localStatusText;
    public string LocalVisualState => _localVisualState;

    private readonly Dictionary<long, int> _multipliersBySteamId = new();
    private readonly Dictionary<long, float> _nextPressTimeBySteamId = new();

    private int _localMultiplier = 1;
    private string _localStatusText = GameLocalization.Phrase( "ui.common.ready", "Ready" );
    private string _localVisualState = "idle";

    private bool _pendingReveal;
    private bool _pendingWin;
    private int _pendingMultiplier;
    private int _pendingPayout;
    private TimeUntil _pendingRevealTimer;

    public bool Press( IPressable.Event e )
    {
        if ( !TryGetPlayerFromPress( e, out var player ) )
            return false;

        if ( player.IsProxy )
            return false;

        if ( Networking.IsHost )
        {
            ProcessSpinOnHost( player, player.GameObject.Network.Owner );
            return true;
        }

        RpcRequestSpin( GameObject );
        return true;
    }

    [Rpc.Host]
    private void RpcRequestSpin( GameObject slotMachineGo )
    {
        if ( !Networking.IsHost )
            return;

        var slotMachine = slotMachineGo.Components.Get<SlotMachine>();
        if ( slotMachine is null )
            return;

        var caller = Rpc.Caller;
        if ( caller is null )
            return;

        var player = slotMachine.FindPlayerBySteamId( caller.SteamId.Value );
        if ( player is null )
            return;

        slotMachine.ProcessSpinOnHost( player, caller );
    }

    private void ProcessSpinOnHost( Player player, Connection connection )
    {
        if ( !Networking.IsHost || player is null || connection is null )
            return;

        if ( !CanPlayerUseMachine( player ) )
        {
            SendRejectedToOwner( connection, GameLocalization.Phrase( "notify.casino.too_far", "Too far" ) );
            return;
        }

        var steamId = connection.SteamId.Value;
        if ( IsOnCooldown( steamId ) )
        {
            SendRejectedToOwner( connection, GameLocalization.Format( "notify.casino.wait_seconds", "Wait {0:0.##}s", PressCooldownSeconds ) );
            return;
        }

        if ( BetCost <= 0 )
        {
            SendRejectedToOwner( connection, GameLocalization.Phrase( "notify.casino.invalid_bet", "Invalid bet" ) );
            return;
        }

        if ( player.Money < BetCost )
        {
            SendRejectedToOwner( connection, GameLocalization.Phrase( "ui.shop.not_enough_money", "Not enough money" ) );
            return;
        }

        player.Money -= BetCost;

        SetCooldown( steamId );

        var currentMultiplier = GetMultiplierForPlayer( steamId );
        var didWin = Game.Random.Float() <= WinChance;

        var newMultiplier = didWin
            ? Math.Min( MaxMultiplier, currentMultiplier + 1 )
            : 1;

        var payout = didWin ? BetCost * newMultiplier : 0;

        _multipliersBySteamId[steamId] = newMultiplier;

        if ( payout > 0 )
            player.Money += payout;

        var revealDelay = Math.Min( PressCooldownSeconds, Math.Max( 0f, SpinDuration ) );

        using ( Rpc.FilterInclude( c => c.SteamId.Value == steamId ) )
        {
            RpcOwnerStartSpin( didWin, newMultiplier, payout, revealDelay );
        }
    }

    [Rpc.Broadcast]
    private void RpcOwnerStartSpin( bool didWin, int newMultiplier, int payout, float revealDelay )
    {
        _localVisualState = "spinning";
        _localStatusText = GameLocalization.Phrase( "ui.casino.spinning", "Spinning..." );

        _pendingWin = didWin;
        _pendingMultiplier = Math.Max( 1, newMultiplier );
        _pendingPayout = Math.Max( 0, payout );

        _pendingReveal = true;
        _pendingRevealTimer = Math.Max( 0.1f, revealDelay );
    }

    [Rpc.Broadcast]
    private void RpcOwnerReject( string reason, int multiplier )
    {
        _pendingReveal = false;
        _localMultiplier = Math.Max( 1, multiplier );
        _localStatusText = reason;
        _localVisualState = "reject";
    }

    private void SendRejectedToOwner( Connection connection, string reason )
    {
        var multiplier = GetMultiplierForPlayer( connection.SteamId.Value );

        using ( Rpc.FilterInclude( c => c.SteamId.Value == connection.SteamId.Value ) )
        {
            RpcOwnerReject( reason, multiplier );
        }
    }

    private int GetMultiplierForPlayer( long steamId )
    {
        if ( _multipliersBySteamId.TryGetValue( steamId, out var mult ) )
            return Math.Max( 1, mult );

        _multipliersBySteamId[steamId] = 1;
        return 1;
    }

    private bool CanPlayerUseMachine( Player player )
    {
        var distance = Vector3.DistanceBetween( player.WorldPosition, WorldPosition );
        return distance <= MaxDistance;
    }

    private bool IsOnCooldown( long steamId )
    {
        return _nextPressTimeBySteamId.TryGetValue( steamId, out var nextAllowedTime )
            && Time.Now < nextAllowedTime;
    }

    private void SetCooldown( long steamId )
    {
        _nextPressTimeBySteamId[steamId] = Time.Now + PressCooldownSeconds;
    }

    private Player FindPlayerBySteamId( long steamId )
    {
        foreach ( var go in Scene.GetAllObjects( true ) )
        {
            if ( !go.Components.TryGet<Player>( out var candidate ) )
                continue;

            if ( candidate.GameObject.Network.Owner?.SteamId.Value == steamId )
                return candidate;
        }

        return null;
    }

    private static bool TryGetPlayerFromPress( IPressable.Event e, out Player player )
    {
        player = null;

        var source = e.Source?.GameObject;
        if ( !source.IsValid() )
            return false;

        return source.Components.TryGet( out player, FindMode.EverythingInSelfAndParent );
    }

    protected override void OnStart()
    {
        _localMultiplier = 1;
        _localStatusText = GameLocalization.Phrase( "ui.common.ready", "Ready" );
        _localVisualState = "idle";
    }

    protected override void OnUpdate()
    {
        if ( !_pendingReveal )
            return;

        if ( !_pendingRevealTimer )
            return;

        _pendingReveal = false;

        _localMultiplier = _pendingMultiplier;
        _localVisualState = _pendingWin ? "win" : "lose";
        _localStatusText = _pendingWin
            ? GameLocalization.Format( "ui.casino.win_money", "Win +${0}", _pendingPayout )
            : GameLocalization.Format( "ui.casino.lose_money", "Lose -${0}", BetCost );

        if ( _pendingWin )
            Notification.SlotWin( _pendingPayout, _localMultiplier );
        else
            Notification.SlotLose( BetCost );
    }
}
