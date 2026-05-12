using Sandbox;
using System;

public sealed class Weed : Component, Component.IPressable
{
    [Property] public float GrowTime { get; set; } = 40f;
    [Property] public int HarvestReward { get; set; } = 100;
    [Property] public float MaxInteractDistance { get; set; } = 100f;
    [Property] public ModelRenderer PlantRenderer { get; set; }

    [Sync] public float GrowProgress { get; set; } = 0f;
    [Sync] public bool IsHarvested { get; set; } = false;

    private TimeUntil _timeUntilGrown;

    public bool IsGrown => GrowProgress >= 1f;

    private static readonly Vector3 ScaleSmall  = new Vector3( 0.2f, 0.2f, 0.2f );
    private static readonly Vector3 ScaleMedium = new Vector3( 0.35f, 0.35f, 0.35f );
    private static readonly Vector3 ScaleMature = new Vector3( 0.75f, 0.75f, 0.75f);

    protected override void OnStart()
    {
        if ( !Networking.IsHost ) return;

        _timeUntilGrown = GrowTime;
        GrowProgress = 0f;
        IsHarvested = false;
    }

    protected override void OnFixedUpdate()
    {
        if ( Networking.IsHost && !IsHarvested )
        {
            float remaining = Math.Max( 0f, (float)_timeUntilGrown );
            GrowProgress = Math.Clamp( 1f - remaining / Math.Max( 0.01f, GrowTime ), 0f, 1f );
        }

        UpdateVisual();
    }

    private void UpdateVisual()
    {
        if ( !PlantRenderer.IsValid() ) return;

        Vector3 targetScale = GrowProgress < 0.5f ? ScaleSmall
                            : GrowProgress < 1f   ? ScaleMedium
                            :                       ScaleMature;

        PlantRenderer.GameObject.LocalScale = targetScale;
    }

    [Rpc.Host]
    public void RpcHarvestWeed( GameObject weedGo )
    {
        if ( !Networking.IsHost ) return;

        var weed = weedGo.Components.Get<Weed>();
        if ( weed is null ) return;

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

        if ( ply is null ) return;

        if ( Vector3.DistanceBetween( ply.WorldPosition, weed.WorldPosition ) > MaxInteractDistance ) return;

        if ( weed.IsHarvested || !weed.IsGrown ) return;

        weed.IsHarvested = true;

        // TODO: заменить на выпадение предмета
        ply.TakeBox( weed.HarvestReward );
    }

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
}
