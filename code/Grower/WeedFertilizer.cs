using Sandbox;
using Ambi.Utils;
using System;

public sealed class WeedFertilizer : Component, Component.ICollisionListener, ICustomDamagable
{
    [Property, Sync( SyncFlags.FromHost )] public float GrowthSpeedMultiplier { get; set; } = 2f;
    [Sync( SyncFlags.FromHost )][Property, Group( "Health" )] public float MaxHealth { get; set; } = 50f;
    [Sync( SyncFlags.FromHost )][Property, Group( "Health" )] public float Health { get; set; } = 50f;

    protected override void OnStart()
    {
#if SERVER
        if ( !Networking.IsHost ) return;

        Health = MaxHealth;
#endif
    }

    void Component.ICollisionListener.OnCollisionStart( Collision collision )
    {
#if SERVER
        var obj = collision.Other.GameObject;

        if ( obj.Components.TryGet<Weed>( out var weed, FindMode.EverythingInSelfAndParent ) )
        {
            FertilizeWeed( weed );
        }
#endif
    }

    [Rpc.Host]
    private void FertilizeWeed( Weed weed )
    {
#if SERVER
        if ( !Networking.IsHost ) return;
        if ( !GameObject.IsValid() ) return;
        if ( !weed.IsValid() ) return;

        if ( weed.ApplyGrowthSpeedMultiplier( GrowthSpeedMultiplier ) )
            GameObject.Destroy();
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
}
