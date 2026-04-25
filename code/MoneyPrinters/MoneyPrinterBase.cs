using Sandbox;
using System;

/// <summary>
/// Базовый класс для всех денежных принтеров.
/// Вся логика здесь — наследники только дают название.
/// </summary>
public class MoneyPrinterBase : Component, Component.IPressable, Component.IDamageable
{
    // ─────────────────────────────────────────────
    //  Инспектор — настраивается в редакторе
    // ─────────────────────────────────────────────

    [Sync( SyncFlags.FromHost )][Property, Group( "Stats" )] public string PrinterName  { get; set; } = "Printer";
    [Sync( SyncFlags.FromHost )][Property, Group( "Stats" )] public int    MoneyPerTick { get; set; } = 10;
    [Sync( SyncFlags.FromHost )][Property, Group( "Stats" )] public float  TickInterval { get; set; } = 5f;
    [Sync( SyncFlags.FromHost )][Property, Group( "Stats" )] public int    MaxMoney     { get; set; } = 5000;
    [Sync( SyncFlags.FromHost )][Property, Group( "Stats" )] public float  MaxDistance  { get; set; } = 100f;

    [Sync( SyncFlags.FromHost )][Property, Group( "Health" )] public float MaxHealth { get; set; } = 100f;

    /// <summary>Таймер для UI. Принтер больше не удаляется при истечении.</summary>
    [Sync( SyncFlags.FromHost )][Property, Group( "Lifetime" )] public float Lifetime { get; set; } = 60f;

    [Property] public ModelRenderer Renderer { get; set; }

    // ─────────────────────────────────────────────
    //  Синхронизированный стейт (виден всем клиентам)
    // ─────────────────────────────────────────────

    [Sync( SyncFlags.FromHost )] public int   StoredMoney { get; protected set; } = 0;
    [Sync( SyncFlags.FromHost )] public bool  IsWorking   { get; protected set; } = true;
    [Sync( SyncFlags.FromHost )] public float Health      { get; protected set; }

    /// <summary>Значение таймера для UI (объект больше не удаляется по времени).</summary>
    [Sync( SyncFlags.FromHost )] public float TimeLeft { get; protected set; }

    // ─────────────────────────────────────────────
    //  Приватный стейт — только хост
    // ─────────────────────────────────────────────

    private TimeUntil _nextTick;
    private TimeUntil _lifetimeTimer;

    // ─────────────────────────────────────────────
    //  Жизненный цикл
    // ─────────────────────────────────────────────

    protected override void OnStart()
    {
        if ( !Networking.IsHost ) return;

        Health         = MaxHealth;
        _nextTick      = TickInterval;
        _lifetimeTimer = Lifetime;
        TimeLeft       = Lifetime;
        IsWorking      = StoredMoney < MaxMoney;

        RefreshVisual();
    }

    protected override void OnFixedUpdate()
    {
        if ( !Networking.IsHost ) return;

        // ── Таймер для UI (без удаления объекта) ──
        if ( Lifetime > 0f )
        {
            TimeLeft = MathF.Max( 0f, _lifetimeTimer );
        }

        // ── Тик печати денег ──
        if ( !IsWorking ) return;
        if ( StoredMoney >= MaxMoney )
        {
            IsWorking = false;
            RefreshVisual();
            return;
        }

        if ( _nextTick )
        {
            StoredMoney = Math.Min( StoredMoney + MoneyPerTick, MaxMoney );
            _nextTick   = TickInterval;

            if ( StoredMoney >= MaxMoney )
                IsWorking = false;

            RefreshVisual();
        }
    }

    // ─────────────────────────────────────────────
    //  Визуал куба
    // ─────────────────────────────────────────────

    [Rpc.Broadcast]
    private void RpcRefreshVisual()
    {
        if ( !Renderer.IsValid() ) return;

        //if      ( !IsWorking              ) Renderer.Tint = Color.Gray;
        //else if ( StoredMoney >= MaxMoney ) Renderer.Tint = new Color( 1f, 0.5f, 0f );
        //else                               Renderer.Tint = new Color( 0.2f, 0.85f, 0.3f );
    }

    protected void RefreshVisual() => RpcRefreshVisual();

    // ─────────────────────────────────────────────
    //  IDamageable
    // ─────────────────────────────────────────────

    public void OnDamage( in DamageInfo dmgInfo )
    {
        if ( !Networking.IsHost ) return;

        Health = MathF.Max( 0f, Health - dmgInfo.Damage );
        RefreshVisual();

        if ( Health <= 0f )
        {
            Log.Info( $"{PrinterName}: destroyed by damage" );
            GameObject.Destroy();
        }
    }

    // ─────────────────────────────────────────────
    //  IPressable — клиент нажал E
    // ─────────────────────────────────────────────

    public bool Press( IPressable.Event e )
    {
        Log.Info( $"{PrinterName}: Press called" );

        if ( StoredMoney <= 0 )
        {
            Log.Info( $"{PrinterName}: no money stored yet" );
            return false;
        }

        var go = e.Source.GameObject;

        if ( !go.Components.TryGet<Player>( out var ply, FindMode.EverythingInSelfAndParent ) )
        {
            Log.Info( $"{PrinterName}: Player not found on source" );
            return false;
        }

        if ( ply.IsProxy )
        {
            Log.Info( $"{PrinterName}: IsProxy, skip" );
            return false;
        }

        RpcOnTakeMoney( GameObject );
        return true;
        }

    // ─────────────────────────────────────────────
    //  RPC Host — авторитетная выдача денег
    // ─────────────────────────────────────────────

    [Rpc.Host]
    public void RpcOnTakeMoney( GameObject printerGo )
    {
        if ( !Networking.IsHost ) return;

        var printer = printerGo.Components.Get<MoneyPrinterBase>();

        if ( printer is null )
        {
            Log.Warning( "RpcOnTakeMoney: printer component not found" );
            return;
        }

        Player ply = null;

        foreach ( var go in Scene.GetAllObjects( true ) )
        {
            if ( !go.Components.TryGet<Player>( out var candidate ) ) 
                continue;

            if ( candidate.GameObject.Network.Owner.SteamId == Rpc.Caller.SteamId )
            {
                ply = candidate;
                break;
            }
        }

        if ( ply is null )
        {
            Log.Warning( $"RpcOnTakeMoney: player not found for {Rpc.Caller.DisplayName}" );
            return;
        }

        var dist = Vector3.DistanceBetween( ply.WorldPosition, printer.WorldPosition );
        if ( dist > printer.MaxDistance )
        {
            Log.Warning( $"RpcOnTakeMoney: {Rpc.Caller.DisplayName} too far ({dist:F0})" );
            return;
        }

        if ( printer.StoredMoney <= 0 )
        {
            Log.Warning( "RpcOnTakeMoney: no money stored" );
            return;
        }

        var payout = printer.StoredMoney;
        printer.StoredMoney = 0;
        printer.IsWorking = true;
        printer._nextTick = printer.TickInterval;

        ply.TakeBox( payout );
        printer.RefreshVisual();

        Log.Info( $"{printer.PrinterName}: {Rpc.Caller.DisplayName} collected ${payout}" );
    }
}
