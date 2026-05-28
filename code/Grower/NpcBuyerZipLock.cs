using Sandbox;
using Ambi.Storage;
using System;

public sealed class NpcBuyerZipLock : Component, Component.IPressable
{
    public const string ZipLockItemId = "ziplock";
    public const int DefaultPricePerZipLock = 100;

    [Property, Sync( SyncFlags.FromHost )] public int PricePerZipLock { get; set; } = DefaultPricePerZipLock;
    [Property, Sync( SyncFlags.FromHost )] public float MaxInteractDistance { get; set; } = 120f;

    public bool Press( IPressable.Event e )
    {
        var source = e.Source?.GameObject;
        if ( !source.IsValid() ) return false;

        if ( !source.Components.TryGet<Player>( out var player, FindMode.EverythingInSelfAndParent ) )
            return false;

        if ( player.IsProxy )
            return false;

        ZipLockSellPanel.Open( this );
        return true;
    }

    [Rpc.Host]
    public static void RpcSellZipLocks( GameObject buyerObject, int amount )
    {
        if ( !Networking.IsHost ) return;

        var caller = Rpc.Caller;
        if ( caller is null ) return;

        if ( !buyerObject.IsValid() )
        {
            NotifySeller( caller, GameLocalization.Phrase( "notify.ziplock_sell.buyer_missing", "Buyer is unavailable." ), false );
            return;
        }

        var buyer = buyerObject.Components.Get<NpcBuyerZipLock>();
        if ( !buyer.IsValid() )
        {
            NotifySeller( caller, GameLocalization.Phrase( "notify.ziplock_sell.buyer_missing", "Buyer is unavailable." ), false );
            return;
        }

        var player = Player.FindPlayerBySteamId( caller.SteamId.Value );
        if ( !player.IsValid() || player.GameObject.Network.Owner != caller )
        {
            NotifySeller( caller, GameLocalization.Phrase( "notify.player.not_ready", "Your player is not ready." ), false );
            return;
        }

        if ( amount <= 0 )
        {
            NotifySeller( caller, GameLocalization.Phrase( "notify.money.invalid_amount", "Enter a valid amount." ), false );
            return;
        }

        if ( Vector3.DistanceBetween( player.WorldPosition, buyer.WorldPosition ) > MathF.Max( 1f, buyer.MaxInteractDistance ) )
        {
            NotifySeller( caller, GameLocalization.Phrase( "notify.ziplock_sell.too_far", "Too far from the buyer." ), false );
            return;
        }

        var inventory = player.Inventory;
        if ( inventory is null )
        {
            NotifySeller( caller, GameLocalization.Phrase( "notify.player.not_ready", "Your player is not ready." ), false );
            return;
        }

        var available = inventory.GetTotalCount( ZipLockItemId );
        if ( available <= 0 )
        {
            NotifySeller( caller, GameLocalization.Phrase( "notify.ziplock_sell.no_items", "You have no ZipLock." ), false );
            return;
        }

        if ( amount > available )
        {
            NotifySeller( caller, GameLocalization.Format( "notify.ziplock_sell.not_enough", "You only have {0} ZipLock.", available ), false );
            return;
        }

        var removed = inventory.RemoveItem( ZipLockItemId, amount );
        if ( removed != amount )
        {
            if ( removed > 0 )
                inventory.AddItem( Item.Create( ZipLockItemId, removed ) );

            NotifySeller( caller, GameLocalization.Phrase( "notify.ziplock_sell.failed", "Sale failed." ), false );
            return;
        }

        var price = Math.Max( 0, buyer.PricePerZipLock );
        var payout = (int)Math.Clamp( (long)amount * price, 0L, int.MaxValue );
        player.Money = (int)Math.Clamp( (long)player.Money + payout, 0L, int.MaxValue );

        NotifySeller( caller, GameLocalization.Format( "notify.ziplock_sell.sold", "Sold {0} ZipLock for ${1}.", amount, payout ), true );
    }

    private static void NotifySeller( Connection connection, string message, bool success )
    {
        if ( connection is null ) return;

        using ( Rpc.FilterInclude( c => c.SteamId.Value == connection.SteamId.Value ) )
        {
            RpcReceiveZipLockSellResult( message, success );
        }
    }

    [Rpc.Broadcast]
    private static void RpcReceiveZipLockSellResult( string message, bool success )
    {
        if ( success )
            Notification.Info( message, 3.5f );
        else
            Notification.Error( message, 3.5f );
    }
}
