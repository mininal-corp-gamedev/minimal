using Sandbox;

public sealed class FadingDoor : Component, IDoorHackable
{
	[Sync( SyncFlags.FromHost )] public Player PlayerOwner { get; private set; }

	[Property, Category( "Lockpick" )] public float LockpickInteractRange { get; set; } = 120f;

	public string DoorHackName => "Fading Door";
	public Vector3 DoorHackWorldPosition => WorldPosition;
	public float DoorHackInteractRange => LockpickInteractRange;

	public bool IsOpen => GetPropCustom()?.FadingDoorIsOpen ?? false;

	private PropCustom GetPropCustom()
	{
		return GameObject.Components.Get<PropCustom>( FindMode.EverythingInSelfAndAncestors );
	}

	private Player EffectiveOwner
	{
		get
		{
			if ( PlayerOwner.IsValid() )
				return PlayerOwner;

			var prop = GetPropCustom();
			return prop.IsValid() ? prop.PlayerOwner : null;
		}
	}

	public void SetOwner( Player owner )
	{
#if SERVER
		if ( !Networking.IsHost )
			return;

		PlayerOwner = owner;
#endif
	}

	public void Open()
	{
#if SERVER
		if ( !Networking.IsHost )
			return;

		GetPropCustom()?.HostSetFadingDoorOpen( true );
#endif
	}

	public void Close()
	{
#if SERVER
		if ( !Networking.IsHost )
			return;

		GetPropCustom()?.HostSetFadingDoorOpen( false );
#endif
	}

#if SERVER
	public bool HostToggleFromPlayer( Player player )
	{
		var prop = GetPropCustom();
		if ( !prop.IsValid() )
			return false;

		return prop.HostToggleFadingDoor( player );
	}

	public static void NotifyPlayerToggle( Player player, bool opened )
	{
		if ( !Networking.IsHost || !player.IsValid() )
			return;

		var connection = player.GameObject.Network.Owner;
		if ( connection is null )
			return;

		var text = opened
			? GameLocalization.Phrase( "notify.fading_door.opened", "Fading Door opened." )
			: GameLocalization.Phrase( "notify.fading_door.closed", "Fading Door closed." );

		using ( Rpc.FilterInclude( c => c.SteamId.Value == connection.SteamId.Value ) )
			RpcShowToggleNotification( text );
	}
#endif

	[Rpc.Broadcast]
	private static void RpcShowToggleNotification( string text )
	{
		Notification.Info( text, 2.5f );
	}

	[Rpc.Host]
	public void RpcRequestLockpick()
	{
#if SERVER
		DoorHackSystem.HostRequestStartHackFromRpc( GameObject );
#endif
	}

	public void RequestDoorHack()
	{
		RpcRequestLockpick();
	}

	public bool CanBeDoorHacked( Player hacker )
	{
		if ( IsOpen ) return false;
		var owner = EffectiveOwner;
		if ( !owner.IsValid() ) return false;
		if ( hacker.IsValid() && hacker.IsArrested ) return false;
		return true;
	}

	public void HostOnDoorHackSucceeded( Player hacker )
	{
#if SERVER
		if ( !Networking.IsHost ) return;
		if ( !CanBeDoorHacked( hacker ) ) return;

		Open();
		NotifyPlayerToggle( hacker, opened: true );
#endif
	}

	public void HostOnDoorHackFailed( Player hacker )
	{
#if SERVER
		if ( !Networking.IsHost ) return;
#endif
	}
}
