using Sandbox;
using Ambi.Storage;
using Ambi.Utils;
using System;

public sealed class Weed : Component, Component.IPressable, ICustomDamagable
{
    [Property] public float GrowTime { get; set; } = 40f;
    [Property] public string HarvestItemId { get; set; } = "ziplock";
    [Property] public int HarvestAmount { get; set; } = 1;
    [Property] public float MaxInteractDistance { get; set; } = 100f;
    [Property] public ModelRenderer PlantRenderer { get; set; }
    [Property, Group( "Growth" )] public float StartPlantScale { get; set; } = 0.2f;
    [Property, Group( "Growth" )] public float MaturePlantScale { get; set; } = 0.45f;

    [Sync( SyncFlags.FromHost )][Property, Group( "Health" )] public float MaxHealth { get; set; } = 100f;
    [Sync( SyncFlags.FromHost )][Property, Group( "Health" )] public float Health { get; set; } = 100f;

    [Sync] public float GrowProgress { get; set; } = 0f;
    [Sync] public bool IsHarvested { get; set; } = false;
    [Sync( SyncFlags.FromHost )] public Player PlayerOwner { get; private set; }

    private TimeUntil _timeUntilGrown;

    public bool IsGrown => GrowProgress >= 1f;

    protected override void OnStart()
    {
#if SERVER
        if ( !Networking.IsHost ) return;

        Health = MaxHealth;
        _timeUntilGrown = GrowTime;
        GrowProgress = 0f;
        IsHarvested = false;
#endif
    }

    protected override void OnFixedUpdate()
    {
        if ( Networking.IsHost && !IsHarvested )
        {
#if SERVER
            UpdateGrowProgressFromTimer();
#endif
        }

        UpdateVisual();
    }

    public void SetOwner( Player owner )
    {
#if SERVER
        if ( !Networking.IsHost ) return;
        PlayerOwner = owner;
#endif
    }

    public bool ApplyGrowthSpeedMultiplier( float multiplier )
    {
#if SERVER
        if ( !Networking.IsHost ) return false;
        if ( IsHarvested || IsGrown ) return false;

        multiplier = MathF.Max( 1f, multiplier );
        if ( multiplier <= 1f ) return false;

        var remaining = MathF.Max( 0f, (float)_timeUntilGrown );
        if ( remaining <= 0f ) return false;

        _timeUntilGrown = remaining / multiplier;
        UpdateGrowProgressFromTimer();
        return true;
#else
        return false;
#endif
    }

#if SERVER
    private void UpdateGrowProgressFromTimer()
    {
        float remaining = MathF.Max( 0f, (float)_timeUntilGrown );
        GrowProgress = Math.Clamp( 1f - remaining / MathF.Max( 0.01f, GrowTime ), 0f, 1f );
    }
#endif

    private void UpdateVisual()
    {
        if ( !PlantRenderer.IsValid() ) return;

        var progress = Math.Clamp( GrowProgress, 0f, 1f );
        var scale = StartPlantScale + (MaturePlantScale - StartPlantScale) * progress;

        PlantRenderer.GameObject.LocalScale = new Vector3( scale, scale, scale );
    }

    [Rpc.Host]
    public void RpcHarvestWeed( GameObject weedGo )
    {
#if SERVER
        if ( !Networking.IsHost ) return;

        var weed = weedGo.Components.Get<Weed>();
        if ( weed is null ) return;

        var caller = Rpc.Caller;
        if ( caller is null ) return;

        Player ply = null;
        foreach ( var go in Scene.GetAllObjects( true ) )
        {
            if ( !go.Components.TryGet<Player>( out var candidate ) ) continue;
            if ( candidate.GameObject.Network.Owner?.SteamId.Value == caller.SteamId.Value )
            {
                ply = candidate;
                break;
            }
        }

        if ( ply is null ) return;

        if ( Vector3.DistanceBetween( ply.WorldPosition, weed.WorldPosition ) > MaxInteractDistance ) return;

        if ( weed.IsHarvested || !weed.IsGrown ) return;

        var itemDefinition = ItemDatabase.Get( weed.HarvestItemId );
        if ( itemDefinition is null )
        {
            NotifyHarvester( caller, GameLocalization.Phrase( "notify.weed.harvest_failed", "Harvest failed." ), false );
            return;
        }

        var item = Item.Create( weed.HarvestItemId, Math.Max( 1, weed.HarvestAmount ) );
        if ( !ply.HostAddItem( item ) )
        {
            NotifyHarvester( caller, GameLocalization.Phrase( "notify.inventory.full", "Inventory is full." ), false );
            return;
        }

        weed.IsHarvested = true;
        NotifyHarvester( caller, GameLocalization.Format( "notify.weed.harvested", "Harvested: {0} x{1}.", GameLocalization.ItemHeader( itemDefinition ), item.Count ), true );
        weed.GameObject.Destroy();
#endif
    }

    public void OnDamage( in DamageInfo dmgInfo )
    {
        if ( Networking.IsHost )
        {
#if SERVER
            ApplyDamage( dmgInfo.Damage );
#endif
            return;
        }

        RpcApplyDamage( dmgInfo.Damage );
    }

    [Rpc.Host]
    private void RpcApplyDamage( float damage )
    {
#if SERVER
        if ( !Networking.IsHost ) return;
        ApplyDamage( damage );
#endif
    }

#if SERVER
    private void ApplyDamage( float damage )
    {
        if ( damage <= 0f ) return;
        if ( Health <= 0f ) return;

        Health = MathF.Max( 0f, Health - damage );
        if ( Health <= 0f )
            GameObject.Destroy();
    }
#endif

    public bool Press( IPressable.Event e )
    {
        var go = e.Source.GameObject;
        if ( !go.Components.TryGet<Player>( out var ply, FindMode.EverythingInSelfAndParent ) ) return false;
        if ( ply.IsProxy ) return false;

        if ( !IsGrown )
        {
            int secondsLeft = (int)Math.Ceiling( (1f - GrowProgress) * GrowTime );
            Notification.Error( GameLocalization.Format( "notify.weed.not_grown", "The plant is not mature yet. Wait {0} sec.", secondsLeft ), 3f );
            return false;
        }

        if ( IsHarvested ) return false;

        RpcHarvestWeed( GameObject );
        return true;
    }

#if SERVER
    private static void NotifyHarvester( Connection connection, string message, bool success )
    {
        if ( connection is null ) return;

        using ( Rpc.FilterInclude( c => c.SteamId.Value == connection.SteamId.Value ) )
        {
            RpcReceiveWeedNotification( message, success );
        }
    }
#endif

    [Rpc.Broadcast]
    private static void RpcReceiveWeedNotification( string message, bool success )
    {
        if ( success )
            Notification.Info( message, 3.5f );
        else
            Notification.Error( message, 3.5f );
    }
}
