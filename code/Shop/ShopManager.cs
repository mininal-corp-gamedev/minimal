using Sandbox;
using Ambi.Storage;
using Minimal.Shop;

public sealed partial class ShopManager : Component
{
	[Rpc.Host]
	public static void RpcRequestPurchase( string shopId )
	{
#if SERVER
		RpcRequestPurchaseServer( shopId );
#endif
	}

	/// <summary>
	/// Хост: удалить все заспавненные объекты игрока, у которых
	/// <see cref="ShopDefinition.HasRemoveAfterChangeJob"/> = true.
	/// Вызывается при смене работы игрока.
	/// </summary>
	public static void RemoveShopObjectsOnJobChange( Player player )
	{
#if SERVER
		RemoveShopObjectsOnJobChangeServer( player );
#endif
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
			Notification.Info( message, 3.5f );
			return;
		}

		Notification.Error( message, 3.5f );
	}
}
