using Minimal.Shop;

public static class ShopDatabase
{
	private static Dictionary<string, ShopDefinition> _cache;

	public static ShopDefinition Get( string id )
	{
		EnsureLoaded();

		var normalized = NormalizeId( id );
		if ( !_cache!.TryGetValue( normalized, out var def ) )
		{
			Log.Error( $"ShopDefinition '{normalized}' not found" );
			return null;
		}

		return def;
	}

	public static IReadOnlyCollection<ShopDefinition> All
	{
		get
		{
			EnsureLoaded();
			return _cache!.Values;
		}
	}

	private static void EnsureLoaded()
	{
		if ( _cache != null )
			return;

		_cache = new();

		foreach ( var shop in ResourceLibrary.GetAll<ShopDefinition>() )
		{
			var id = NormalizeId( shop.Id );
			if ( string.IsNullOrWhiteSpace( id ) )
			{
				Log.Warning( $"ShopDatabase: shop '{shop.Header}' has empty Id." );
				continue;
			}

			if ( _cache.TryGetValue( id, out var existing ) )
			{
				Log.Warning( $"ShopDatabase: duplicate shop id '{id}' ({existing.Header} vs {shop.Header})." );
				continue;
			}

			_cache[id] = shop;
		}
	}

	private static string NormalizeId( string id )
	{
		return (id ?? string.Empty).Trim();
	}
}
