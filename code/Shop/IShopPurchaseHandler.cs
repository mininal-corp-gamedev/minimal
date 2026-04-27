using Minimal.Shop;

public interface IShopPurchaseHandler
{
	string ShopId { get; }
	bool PostPurchased( ShopPurchaseContext context );
}

public sealed class ShopPurchaseContext
{
	public ShopDefinition Shop { get; init; }
	public Player Buyer { get; init; }
	public Connection Connection { get; init; }
}
