public sealed class BoomboxShopHandler : IShopPurchaseHandler
{
	public string ShopId => "boombox";

	private const float SpawnDistance = 60f;
	private const float SpawnHeight   = 34f;

	public bool PostPurchased( ShopPurchaseContext context )
	{
		if ( !Networking.IsHost )
			return false;

		var buyer = context?.Buyer;
		var shop  = context?.Shop;
		if ( !buyer.IsValid() || shop is null )
			return false;

		if ( !shop.SpawnPrefab.IsValid() )
		{
			Log.Warning( "BoomboxShopHandler: SpawnPrefab is not set." );
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
		var boomboxObject = shop.SpawnPrefab.Clone( spawnPosition, Rotation.LookAt( yawForward.Normal ) );
		if ( !boomboxObject.IsValid() )
			return false;

		var shopObj = boomboxObject.AddComponent<ShopObject>();
		shopObj.SetOwner( buyer );
		shopObj.Definition = shop;

		var boombox = boomboxObject.Components.Get<Boombox>( FindMode.EverythingInSelfAndDescendants );
		if ( !boombox.IsValid() )
			Log.Warning( "BoomboxShopHandler: spawned prefab without Boombox component." );

		boomboxObject.NetworkSpawn();
		OwnedPropNetwork.ConfigureShopObject( boomboxObject );
		return true;
	}
}
