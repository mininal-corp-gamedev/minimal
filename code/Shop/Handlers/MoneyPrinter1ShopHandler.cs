public sealed class MoneyPrinter1ShopHandler : IShopPurchaseHandler
{
	public string ShopId => "money_printer1";

	private const float SpawnDistance = 60f;
	private const float SpawnHeight = 24f;

	public bool PostPurchased( ShopPurchaseContext context )
	{
		if ( !Networking.IsHost )
			return false;

		var buyer = context?.Buyer;
		var shop = context?.Shop;
		if ( !buyer.IsValid() || shop is null )
			return false;

		if ( !shop.SpawnPrefab.IsValid() )
		{
			Log.Warning( "MoneyPrinter1ShopHandler: SpawnPrefab is not set." );
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
		var printerObject = shop.SpawnPrefab.Clone( spawnPosition, Rotation.LookAt( yawForward.Normal ) );
		if ( !printerObject.IsValid() )
			return false;

		var printer = printerObject.Components.Get<MoneyPrinterBase>( FindMode.EverythingInSelfAndDescendants );
		if ( printer.IsValid() )
			printer.SetOwner( buyer );

		// PropCustom мы НЕ ставим намеренно: с ним physgun (ЛКМ) разрешил бы захват
		// принтера, а по требованию принтер должен браться только gravitygun-ом (ПКМ).
		// Доступ для gravitygun обеспечивается отдельной веткой в WeaponPhysgun
		// (по наличию MoneyPrinterBase + Network.Owner).

		// Сетевого владельца назначаем покупателю, иначе он не сможет
		// манипулировать физикой принтера (physgun/gravitygun проверяют
		// rb.GameObject.Network.Owner == Connection.Local).
		// Берём Connection из контекста (как PropsMenu использует caller).
		printerObject.NetworkSpawn( context.Connection );
		return true;
	}
}
