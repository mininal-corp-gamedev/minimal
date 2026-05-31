using Sandbox;

public sealed class Atm : Component, Component.IPressable
{
	public bool Press( IPressable.Event e )
	{
		var go = e.Source.GameObject;
		if ( !go.Components.TryGet<Player>( out var ply, FindMode.EverythingInSelfAndParent ) )
			return false;
		if ( ply.IsProxy )
			return false;

		AtmPanel.Open( this );
		return true;
	}

	public void RequestDeposit( int amount )
	{
		var local = Player.Local;
		if ( !local.IsValid() ) return;

		if ( amount <= 0 )
		{
			Notification.Make( GameLocalization.Phrase( "notify.atm.invalid_deposit", "Invalid deposit amount." ), 3f );
			return;
		}

		if ( local.Money < amount )
		{
			Notification.Make( GameLocalization.Phrase( "notify.atm.not_enough_cash", "Not enough cash." ), 3f );
			return;
		}

		if ( Networking.IsHost )
		{
#if SERVER
			HostDeposit( local, amount );
#endif
			return;
		}

		RpcRequestDeposit( amount );
	}

	public void RequestWithdraw( int amount )
	{
		var local = Player.Local;
		if ( !local.IsValid() ) return;

		if ( amount <= 0 )
		{
			Notification.Make( GameLocalization.Phrase( "notify.atm.invalid_withdraw", "Invalid withdraw amount." ), 3f );
			return;
		}

		if ( local.MoneyAtm < amount )
		{
			Notification.Make( GameLocalization.Phrase( "notify.atm.not_enough_account", "Not enough funds in account." ), 3f );
			return;
		}

		if ( Networking.IsHost )
		{
#if SERVER
			HostWithdraw( local, amount );
#endif
			return;
		}

		RpcRequestWithdraw( amount );
	}

	[Rpc.Host]
	private void RpcRequestDeposit( int amount )
	{
#if SERVER
		if ( !Networking.IsHost ) return;

		var caller = Rpc.Caller;
		if ( caller is null ) return;

		var player = Player.FindPlayerBySteamId( caller.SteamId.Value );
		if ( !player.IsValid() )
		{
			NotifyAtmResult( caller, GameLocalization.Phrase( "notify.player.not_ready", "Your player is not ready." ), false );
			return;
		}

		if ( player.GameObject.Network.Owner != caller )
		{
			NotifyAtmResult( caller, GameLocalization.Phrase( "notify.atm.auth_error", "Authorization error." ), false );
			return;
		}

		if ( amount <= 0 )
		{
			NotifyAtmResult( caller, GameLocalization.Phrase( "notify.atm.invalid_deposit", "Invalid deposit amount." ), false );
			return;
		}

		if ( player.Money < amount )
		{
			NotifyAtmResult( caller, GameLocalization.Phrase( "notify.atm.not_enough_cash", "Not enough cash." ), false );
			return;
		}

		HostDeposit( player, amount );
#endif
	}

	[Rpc.Host]
	private void RpcRequestWithdraw( int amount )
	{
#if SERVER
		if ( !Networking.IsHost ) return;

		var caller = Rpc.Caller;
		if ( caller is null ) return;

		var player = Player.FindPlayerBySteamId( caller.SteamId.Value );
		if ( !player.IsValid() )
		{
			NotifyAtmResult( caller, GameLocalization.Phrase( "notify.player.not_ready", "Your player is not ready." ), false );
			return;
		}

		if ( player.GameObject.Network.Owner != caller )
		{
			NotifyAtmResult( caller, GameLocalization.Phrase( "notify.atm.auth_error", "Authorization error." ), false );
			return;
		}

		if ( amount <= 0 )
		{
			NotifyAtmResult( caller, GameLocalization.Phrase( "notify.atm.invalid_withdraw", "Invalid withdraw amount." ), false );
			return;
		}

		if ( player.MoneyAtm < amount )
		{
			NotifyAtmResult( caller, GameLocalization.Phrase( "notify.atm.not_enough_account", "Not enough funds in account." ), false );
			return;
		}

		HostWithdraw( player, amount );
#endif
	}

#if SERVER
	private void HostDeposit( Player player, int amount )
	{
		if ( !Networking.IsHost ) return;

		player.Money -= amount;
		player.MoneyAtm += amount;

		var conn = player.GameObject.Network.Owner;
		Log.Info( $"[ATM] {conn?.DisplayName} deposited ${amount}. ATM balance: ${player.MoneyAtm}" );
		NotifyAtmResult( conn, GameLocalization.Format( "notify.atm.deposited", "Deposited ${0}. Account: ${1}", amount, player.MoneyAtm ), true );
	}

	private void HostWithdraw( Player player, int amount )
	{
		if ( !Networking.IsHost ) return;

		player.MoneyAtm -= amount;
		player.Money += amount;

		var conn = player.GameObject.Network.Owner;
		Log.Info( $"[ATM] {conn?.DisplayName} withdrew ${amount}. ATM balance: ${player.MoneyAtm}" );
		NotifyAtmResult( conn, GameLocalization.Format( "notify.atm.withdrew", "Withdrew ${0}. Account: ${1}", amount, player.MoneyAtm ), true );
	}
#endif

#if SERVER
	private static void NotifyAtmResult( Connection connection, string message, bool success )
	{
		if ( connection is null ) return;

		using ( Rpc.FilterInclude( c => c.SteamId.Value == connection.SteamId.Value ) )
		{
			RpcReceiveAtmResult( message, success );
		}
	}
#endif

	[Rpc.Broadcast]
	private static void RpcReceiveAtmResult( string message, bool success )
	{
		if ( success )
			Notification.Info( message, 3.5f );
		else
			Notification.Error( message, 3.5f );
	}
}
