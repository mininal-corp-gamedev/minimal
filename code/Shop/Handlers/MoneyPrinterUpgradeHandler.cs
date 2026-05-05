public sealed class MoneyPrinterUpgradeHandler : IShopPurchaseHandler
{
    public string ShopId => "money_printer_upgrade";

    private const float SpawnDistance = 60f;
    private const float SpawnHeight = 24f;

    public bool PostPurchased(ShopPurchaseContext context)
    {
        if (!Networking.IsHost)
            return false;

        var buyer = context?.Buyer;
        var shop = context?.Shop;
        if (!buyer.IsValid() || shop is null)
            return false;

        var forward = buyer.Controller.IsValid()
            ? buyer.Controller.EyeTransform.Forward
            : buyer.WorldRotation.Forward;

        var yawForward = new Vector3(forward.x, forward.y, 0f);
        if (yawForward.LengthSquared <= 0.001f)
            yawForward = buyer.WorldRotation.Forward;

        yawForward = yawForward.Normal;
        var spawnPosition = buyer.WorldPosition + yawForward * SpawnDistance + Vector3.Up * SpawnHeight;

        var printerObject = shop.SpawnPrefab.Clone(spawnPosition, Rotation.LookAt(yawForward.Normal));
        if (!printerObject.IsValid())
            return false;

        var shopObj = printerObject.AddComponent<ShopObject>();
        shopObj.PlayerOwner = buyer;
        shopObj.Definition = shop;

        printerObject.NetworkSpawn(context.Buyer.Network.Owner);

        return true;
    }
}