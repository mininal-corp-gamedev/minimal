using Sandbox;

/// <summary>
/// Host-only network setup for spawned props and shop objects.
/// Gameplay owner is <see cref="PropCustom.PlayerOwner"/> / <see cref="ShopObject.PlayerOwner"/>;
/// network owner starts on the server until a physgun/gravity grab transfers it.
/// </summary>
public static class OwnedPropNetwork
{
	public static void ConfigurePropCustom( GameObject gameObject )
	{
#if SERVER
		if ( !Networking.IsHost || !gameObject.IsValid() )
			return;

		ConfigureOwnedObject( gameObject );
#endif
	}

	public static void ConfigureShopObject( GameObject gameObject )
	{
#if SERVER
		if ( !Networking.IsHost || !gameObject.IsValid() )
			return;

		ConfigureOwnedObject( gameObject );
		gameObject.Network.DropOwnership();
#endif
	}

#if SERVER
	private static void ConfigureOwnedObject( GameObject gameObject )
	{
		gameObject.Network.SetOwnerTransfer( OwnerTransfer.Request );
		gameObject.Network.SetOrphanedMode( NetworkOrphaned.Host );
	}
#endif
}
