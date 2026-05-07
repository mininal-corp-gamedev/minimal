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
			Notification.Make( "Некорректная сумма для внесения.", 3f );
			return;
		}

		if ( local.Money < amount )
		{
			Notification.Make( "Недостаточно наличных.", 3f );
			return;
		}

		if ( Networking.IsHost )
		{
			HostDeposit( local, amount );
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
			Notification.Make( "Некорректная сумма для снятия.", 3f );
			return;
		}

		if ( local.MoneyAtm < amount )
		{
			Notification.Make( "Недостаточно средств на счёте.", 3f );
			return;
		}

		if ( Networking.IsHost )
		{
			HostWithdraw( local, amount );
			return;
		}

		RpcRequestWithdraw( amount );
	}

	[Rpc.Host]
	private void RpcRequestDeposit( int amount )
	{
		if ( !Networking.IsHost ) return;

		var caller = Rpc.Caller;
		if ( caller is null ) return;

		var player = Player.FindPlayerBySteamId( caller.SteamId.Value );
		if ( !player.IsValid() )
		{
			NotifyAtmResult( caller, "Твой игрок ещё не готов.", false );
			return;
		}

		if ( player.GameObject.Network.Owner != caller )
		{
			NotifyAtmResult( caller, "Ошибка авторизации.", false );
			return;
		}

		if ( amount <= 0 )
		{
			NotifyAtmResult( caller, "Некорректная сумма для внесения.", false );
			return;
		}

		if ( player.Money < amount )
		{
			NotifyAtmResult( caller, "Недостаточно наличных.", false );
			return;
		}

		HostDeposit( player, amount );
	}

	[Rpc.Host]
	private void RpcRequestWithdraw( int amount )
	{
		if ( !Networking.IsHost ) return;

		var caller = Rpc.Caller;
		if ( caller is null ) return;

		var player = Player.FindPlayerBySteamId( caller.SteamId.Value );
		if ( !player.IsValid() )
		{
			NotifyAtmResult( caller, "Твой игрок ещё не готов.", false );
			return;
		}

		if ( player.GameObject.Network.Owner != caller )
		{
			NotifyAtmResult( caller, "Ошибка авторизации.", false );
			return;
		}

		if ( amount <= 0 )
		{
			NotifyAtmResult( caller, "Некорректная сумма для снятия.", false );
			return;
		}

		if ( player.MoneyAtm < amount )
		{
			NotifyAtmResult( caller, "Недостаточно средств на счёте.", false );
			return;
		}

		HostWithdraw( player, amount );
	}

	private void HostDeposit( Player player, int amount )
	{
		if ( !Networking.IsHost ) return;

		player.Money -= amount;
		player.MoneyAtm += amount;

		var conn = player.GameObject.Network.Owner;
		Log.Info( $"[ATM] {conn?.DisplayName} deposited ${amount}. ATM balance: ${player.MoneyAtm}" );
		NotifyAtmResult( conn, $"Внесено ${amount}. Счёт: ${player.MoneyAtm}", true );
	}

	private void HostWithdraw( Player player, int amount )
	{
		if ( !Networking.IsHost ) return;

		player.MoneyAtm -= amount;
		player.Money += amount;

		var conn = player.GameObject.Network.Owner;
		Log.Info( $"[ATM] {conn?.DisplayName} withdrew ${amount}. ATM balance: ${player.MoneyAtm}" );
		NotifyAtmResult( conn, $"Снято ${amount}. Счёт: ${player.MoneyAtm}", true );
	}

	private static void NotifyAtmResult( Connection connection, string message, bool success )
	{
		if ( connection is null ) return;

		using ( Rpc.FilterInclude( c => c.SteamId.Value == connection.SteamId.Value ) )
		{
			RpcReceiveAtmResult( message, success );
		}
	}

	[Rpc.Broadcast]
	private static void RpcReceiveAtmResult( string message, bool success )
	{
		if ( success )
			Notification.Info( message, 3.5f );
		else
			Notification.Error( message, 3.5f );
	}
}
