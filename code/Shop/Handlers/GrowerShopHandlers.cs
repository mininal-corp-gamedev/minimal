using Sandbox;

public sealed class GrowerWeedShopHandler : IShopPurchaseHandler
{
    public string ShopId => "grower_weed";

    public bool PostPurchased( ShopPurchaseContext context )
    {
        return GrowerShopSpawner.SpawnOwnedObject( context, configureWeed: true );
    }
}

public sealed class GrowerFertilizerShopHandler : IShopPurchaseHandler
{
    public string ShopId => "grower_fertilizer";

    public bool PostPurchased( ShopPurchaseContext context )
    {
        return GrowerShopSpawner.SpawnOwnedObject( context, configureWeed: false );
    }
}

file static class GrowerShopSpawner
{
    private const float SpawnDistance = 60f;
    private const float SpawnHeight = 24f;

    public static bool SpawnOwnedObject( ShopPurchaseContext context, bool configureWeed )
    {
        if ( !Networking.IsHost )
            return false;

        var buyer = context?.Buyer;
        var shop = context?.Shop;
        if ( !buyer.IsValid() || shop is null )
            return false;

        if ( !shop.SpawnPrefab.IsValid() )
        {
            Log.Warning( $"GrowerShopSpawner: SpawnPrefab is not set for '{shop.Id}'." );
            return false;
        }

        var forward = buyer.Controller.IsValid()
            ? buyer.Controller.EyeTransform.Forward
            : buyer.WorldRotation.Forward;

        var yawForward = new Vector3( forward.x, forward.y, 0f );
        if ( yawForward.LengthSquared <= 0.001f )
            yawForward = buyer.WorldRotation.Forward;

        yawForward = yawForward.Normal;
        var spawnPosition = buyer.WorldPosition + yawForward * SpawnDistance + Vector3.Up * SpawnHeight;

        var spawnedObject = shop.SpawnPrefab.Clone( spawnPosition, Rotation.LookAt( yawForward.Normal ) );
        if ( !spawnedObject.IsValid() )
            return false;

        var shopObj = spawnedObject.AddComponent<ShopObject>();
        shopObj.SetOwner( buyer );
        shopObj.Definition = shop;

        if ( !spawnedObject.Components.Get<Rigidbody>( FindMode.EverythingInSelfAndDescendants ).IsValid() )
            spawnedObject.Components.Create<Rigidbody>();

        if ( configureWeed )
        {
            var weed = spawnedObject.Components.Get<Weed>( FindMode.EverythingInSelfAndDescendants );
            if ( weed.IsValid() )
                weed.SetOwner( buyer );
        }

        spawnedObject.NetworkSpawn();
        OwnedPropNetwork.ConfigureShopObject( spawnedObject );
        return true;
    }
}
