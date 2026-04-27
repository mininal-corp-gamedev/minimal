using Sandbox;
using Ambi.Storage;
using Minimal.Shop;

public sealed class ShopManager : Component
{
	[Rpc.Host]
	public static void RpcRequestPurchase( string shopId )
	{
		if ( !Networking.IsHost )
			return;

		var caller = Rpc.Caller;
		if ( caller is null )
			return;

		var normalized = (shopId ?? string.Empty).Trim();
		if ( string.IsNullOrEmpty( normalized ) )
		{
			NotifyBuyer( caller, false, "Shop id is empty.", null );
			return;
		}

		var shop = ShopDatabase.Get( normalized );
		if ( shop is null )
		{
			NotifyBuyer( caller, false, "Shop item not found.", null );
			return;
		}

		var buyer = FindPlayerBySteamId( caller.SteamId.Value );
		if ( !buyer.IsValid() )
		{
			NotifyBuyer( caller, false, "Your player is not ready.", null );
			return;
		}

		if ( !CanBuyByJob( buyer, shop ) )
		{
			NotifyBuyer( caller, false, "Your job cannot buy this.", null );
			return;
		}

		if ( shop.ItemDefinition is null && !shop.HasPostPurchased )
		{
			NotifyBuyer( caller, false, "Shop item has no purchase action.", null );
			return;
		}

		var price = Math.Max( 0, shop.Price );
		if ( buyer.Money < price )
		{
			NotifyBuyer( caller, false, $"Need ${price}.", null );
			return;
		}

		buyer.Money -= price;

		var context = new ShopPurchaseContext
		{
			Shop = shop,
			Buyer = buyer,
			Connection = caller
		};

		if ( !ShopHandlerRegistry.FirePostPurchased( context ) )
		{
			buyer.Money += price;
			NotifyBuyer( caller, false, "Purchase failed.", null );
			return;
		}

		var itemId = shop.ItemDefinition?.Id;
		NotifyBuyer( caller, true, $"Purchased: {shop.Header}.", itemId );
	}

	public static bool CanBuyByJob( Player buyer, ShopDefinition shop )
	{
		if ( shop is null )
			return false;

		if ( shop.IsAllowEveryone )
			return true;

		if ( !buyer.IsValid() || !buyer.Job.IsValid() || buyer.Job.JobDefinition is null )
			return false;

		if ( shop.JobsAllow is null || shop.JobsAllow.Count == 0 )
			return false;

		var buyerJobId = buyer.Job.JobDefinition.Id;
		return shop.JobsAllow.Any( x => x is not null && string.Equals( x.Id, buyerJobId, StringComparison.Ordinal ) );
	}

	private static Player FindPlayerBySteamId( long steamId )
	{
		var scene = Game.ActiveScene;
		if ( scene is null )
			return null;

		foreach ( var player in scene.GetAllComponents<Player>() )
		{
			if ( player.GameObject.Network.Owner?.SteamId == steamId )
				return player;
		}

		return null;
	}

	private static void NotifyBuyer( Connection buyer, bool success, string message, string itemId )
	{
		using ( Rpc.FilterInclude( c => c.SteamId.Value == buyer.SteamId.Value ) )
		{
			RpcReceivePurchaseResult( success, message, itemId );
		}
	}

	[Rpc.Broadcast]
	private static void RpcReceivePurchaseResult( bool success, string message, string itemId )
	{
		if ( success )
		{
			if ( !string.IsNullOrWhiteSpace( itemId ) )
			{
				var player = Player.Local;
				if ( player?.Inventory is not null && player.Inventory.CanAddItem( itemId, 1 ) )
				{
					player.Inventory.AddItem( Item.Create( itemId, 1 ) );
				}
				else
				{
					Notification.Error( "Inventory is full.", 3.5f );
					return;
				}
			}

			Notification.Info( message, 3.5f );
			return;
		}

		Notification.Error( message, 3.5f );
	}
}
