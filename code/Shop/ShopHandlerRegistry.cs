public static class ShopHandlerRegistry
{
	private static Dictionary<string, IShopPurchaseHandler> _handlers;

	private static void EnsureLoaded()
	{
		if ( _handlers != null )
			return;

		_handlers = new();

		foreach ( var type in TypeLibrary.GetTypes<IShopPurchaseHandler>() )
		{
			if ( type.IsAbstract || type.IsInterface )
				continue;

			if ( TypeLibrary.Create<IShopPurchaseHandler>( type.TargetType ) is not { } handler )
				continue;

			var id = (handler.ShopId ?? string.Empty).Trim();
			if ( string.IsNullOrEmpty( id ) )
				continue;

			if ( _handlers.TryGetValue( id, out var existing ) )
			{
				Log.Warning( $"ShopHandlerRegistry: duplicate handler for '{id}' ({existing.GetType().Name} vs {handler.GetType().Name})." );
				continue;
			}

			_handlers[id] = handler;
		}
	}

	public static IShopPurchaseHandler Get( string shopId )
	{
		EnsureLoaded();
		_handlers.TryGetValue( (shopId ?? string.Empty).Trim(), out var handler );
		return handler;
	}

	internal static bool FirePostPurchased( ShopPurchaseContext context )
	{
		if ( context?.Shop is null || !context.Shop.HasPostPurchased )
			return true;

		var handler = Get( context.Shop.Id );
		if ( handler is null )
		{
			Log.Warning( $"ShopHandlerRegistry: handler for '{context.Shop.Id}' not found." );
			return false;
		}

		return handler.PostPurchased( context );
	}
}
