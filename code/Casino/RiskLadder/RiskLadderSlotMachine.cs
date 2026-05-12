using Sandbox;
using System;
using System.Collections.Generic;

public sealed class RiskLadderSlotMachine : Component, Component.IPressable
{
	private const float MinRevealDelay = 0.1f;

	[Property, Group( "Gameplay" )] public int BetCost { get; set; } = 50;
	[Property, Group( "Gameplay" )] public float StartWinChance { get; set; } = 0.55f;
	[Property, Group( "Gameplay" )] public float ChanceDropPerStep { get; set; } = 0.07f;
	[Property, Group( "Gameplay" )] public float MinWinChance { get; set; } = 0.16f;
	[Property, Group( "Gameplay" )] public int MaxStep { get; set; } = 8;
	[Property, Group( "Gameplay" )] public float FirstWinMultiplier { get; set; } = 1.6f;
	[Property, Group( "Gameplay" )] public float MultiplierStep { get; set; } = 1.0f;
	[Property, Group( "Gameplay" )] public float MultiplierCurve { get; set; } = 0.22f;
	[Property, Group( "Gameplay" )] public float MaxBankMultiplier { get; set; } = 25f;
	[Property, Group( "Gameplay" )] public int BankRoundTo { get; set; } = 5;
	[Property, Group( "Gameplay" )] public float MaxDistance { get; set; } = 120f;
	[Property, Group( "Gameplay" )] public float ActionCooldownSeconds { get; set; } = 0.35f;
	[Property, Group( "Input" )] public string CashoutInput { get; set; } = "Reload";
	[Property, Group( "Input" )] public float CashoutLookDot { get; set; } = 0.35f;
	[Property, Group( "Visual" )] public float SpinDuration { get; set; } = 1.15f;

	public int LocalStep => _localStep;
	public int LocalBank => _localBank;
	public int LocalNextPayout => GetBankForStep( _localStep + 1 );
	public float LocalNextWinChance => GetWinChanceForStep( _localStep + 1 );
	public string LocalStatusText => _localStatusText;
	public string LocalVisualState => _localVisualState;
	public bool LocalHasBank => _localBank > 0;
	public bool LocalAtMaxStep => _localStep >= GetSafeMaxStep();

	private readonly Dictionary<long, RiskLadderSession> _sessionsBySteamId = new();
	private readonly Dictionary<long, float> _nextActionTimeBySteamId = new();

	private int _localStep;
	private int _localBank;
	private string _localStatusText = GameLocalization.Phrase( "ui.common.ready", "Ready" );
	private string _localVisualState = "idle";

	private bool _pendingReveal;
	private bool _pendingWin;
	private bool _pendingStartedRound;
	private int _pendingStep;
	private int _pendingBank;
	private int _pendingLostValue;
	private float _pendingWinChance;
	private TimeUntil _pendingRevealTimer;

	public bool Press( IPressable.Event e )
	{
		if ( !TryGetPlayerFromPress( e, out var player ) )
			return false;

		if ( player.IsProxy )
			return false;

		if ( Networking.IsHost )
		{
			HostRisk( player, player.GameObject.Network.Owner );
			return true;
		}

		RpcRequestRisk( GameObject );
		return true;
	}

	[Rpc.Host]
	private void RpcRequestRisk( GameObject slotMachineGo )
	{
		if ( !Networking.IsHost )
			return;

		var slotMachine = slotMachineGo.Components.Get<RiskLadderSlotMachine>();
		if ( !slotMachine.IsValid() )
			return;

		var caller = Rpc.Caller;
		if ( caller is null )
			return;

		var player = Player.FindPlayerBySteamId( caller.SteamId.Value );
		if ( !player.IsValid() )
			return;

		slotMachine.HostRisk( player, caller );
	}

	[Rpc.Host]
	private void RpcRequestCashout( GameObject slotMachineGo )
	{
		if ( !Networking.IsHost )
			return;

		var slotMachine = slotMachineGo.Components.Get<RiskLadderSlotMachine>();
		if ( !slotMachine.IsValid() )
			return;

		var caller = Rpc.Caller;
		if ( caller is null )
			return;

		var player = Player.FindPlayerBySteamId( caller.SteamId.Value );
		if ( !player.IsValid() )
			return;

		slotMachine.HostCashout( player, caller );
	}

	private void HostRisk( Player player, Connection connection )
	{
		if ( !Networking.IsHost || !player.IsValid() || connection is null )
			return;

		var steamId = connection.SteamId.Value;
		if ( !CanPlayerUseMachine( player ) )
		{
			SendRejectedToOwner( connection, GameLocalization.Phrase( "notify.casino.too_far", "Too far" ) );
			return;
		}

		if ( IsOnCooldown( steamId ) )
		{
			SendRejectedToOwner( connection, GameLocalization.Phrase( "notify.casino.wait", "Wait" ) );
			return;
		}

		if ( BetCost <= 0 )
		{
			SendRejectedToOwner( connection, GameLocalization.Phrase( "notify.casino.invalid_bet", "Invalid bet" ) );
			return;
		}

		var session = GetSession( steamId );
		if ( session.IsSpinning )
		{
			SendRejectedToOwner( connection, GameLocalization.Phrase( "ui.casino.spinning_short", "Spinning" ) );
			return;
		}

		var startedRound = !session.HasBank;
		if ( startedRound && player.Money < BetCost )
		{
			SendRejectedToOwner( connection, GameLocalization.Phrase( "ui.shop.not_enough_money", "Not enough money" ) );
			return;
		}

		if ( !startedRound && session.Step >= GetSafeMaxStep() )
		{
			SendRejectedToOwner( connection, GameLocalization.Phrase( "ui.casino.cashout", "Cashout" ) );
			return;
		}

		if ( startedRound )
			player.Money -= BetCost;

		session.IsSpinning = true;
		_sessionsBySteamId[steamId] = session;
		SetCooldown( steamId, MathF.Max( ActionCooldownSeconds, SpinDuration ) );

		var nextStep = Math.Clamp( session.Step + 1, 1, GetSafeMaxStep() );
		var winChance = GetWinChanceForStep( nextStep );
		var didWin = Game.Random.Float() <= winChance;
		var lostValue = startedRound ? BetCost : session.Bank;

		if ( didWin )
		{
			session.Step = nextStep;
			session.Bank = GetBankForStep( nextStep );
			session.IsSpinning = false;
			_sessionsBySteamId[steamId] = session;
		}
		else
		{
			session = default;
			_sessionsBySteamId.Remove( steamId );
		}

		using ( Rpc.FilterInclude( c => c.SteamId.Value == steamId ) )
		{
			RpcOwnerStartRiskSpin(
				didWin,
				startedRound,
				didWin ? session.Step : 0,
				didWin ? session.Bank : 0,
				lostValue,
				winChance,
				MathF.Max( MinRevealDelay, SpinDuration ) );
		}
	}

	private void HostCashout( Player player, Connection connection )
	{
		if ( !Networking.IsHost || !player.IsValid() || connection is null )
			return;

		var steamId = connection.SteamId.Value;
		if ( !CanPlayerUseMachine( player ) )
		{
			SendRejectedToOwner( connection, GameLocalization.Phrase( "notify.casino.too_far", "Too far" ) );
			return;
		}

		if ( IsOnCooldown( steamId ) )
		{
			SendRejectedToOwner( connection, GameLocalization.Phrase( "notify.casino.wait", "Wait" ) );
			return;
		}

		if ( !_sessionsBySteamId.TryGetValue( steamId, out var session ) || !session.HasBank )
		{
			SendRejectedToOwner( connection, GameLocalization.Phrase( "notify.casino.no_bank", "No bank" ) );
			return;
		}

		if ( session.IsSpinning )
		{
			SendRejectedToOwner( connection, GameLocalization.Phrase( "ui.casino.spinning_short", "Spinning" ) );
			return;
		}

		var payout = Math.Max( 0, session.Bank );
		_sessionsBySteamId.Remove( steamId );
		SetCooldown( steamId, ActionCooldownSeconds );

		if ( payout > 0 )
			player.Money += payout;

		using ( Rpc.FilterInclude( c => c.SteamId.Value == steamId ) )
		{
			RpcOwnerCashout( payout );
		}
	}

	[Rpc.Broadcast]
	private void RpcOwnerStartRiskSpin( bool didWin, bool startedRound, int step, int bank, int lostValue, float winChance, float revealDelay )
	{
		_localVisualState = "spinning";
		_localStatusText = startedRound ? GameLocalization.Phrase( "ui.casino.rolling", "Rolling..." ) : GameLocalization.Phrase( "ui.casino.risking", "Risking..." );

		_pendingWin = didWin;
		_pendingStartedRound = startedRound;
		_pendingStep = Math.Max( 0, step );
		_pendingBank = Math.Max( 0, bank );
		_pendingLostValue = Math.Max( 0, lostValue );
		_pendingWinChance = Math.Clamp( winChance, 0f, 1f );

		_pendingReveal = true;
		_pendingRevealTimer = MathF.Max( MinRevealDelay, revealDelay );
	}

	[Rpc.Broadcast]
	private void RpcOwnerCashout( int payout )
	{
		_pendingReveal = false;
		_localStep = 0;
		_localBank = 0;
		_localStatusText = GameLocalization.Format( "ui.casino.cashout_money", "Cashout +${0}", Math.Max( 0, payout ) );
		_localVisualState = "cashout";

		Notification.Info( GameLocalization.Format( "notify.casino.risk_cashout", "Risk ladder cashout: +${0}", Math.Max( 0, payout ) ), 2.4f );
	}

	[Rpc.Broadcast]
	private void RpcOwnerReject( string reason, int step, int bank )
	{
		if ( _pendingReveal )
		{
			_localStatusText = reason;
			return;
		}

		_pendingReveal = false;
		_localStep = Math.Max( 0, step );
		_localBank = Math.Max( 0, bank );
		_localStatusText = reason;
		_localVisualState = "reject";
	}

	private void SendRejectedToOwner( Connection connection, string reason )
	{
		var session = GetSession( connection.SteamId.Value );

		using ( Rpc.FilterInclude( c => c.SteamId.Value == connection.SteamId.Value ) )
		{
			RpcOwnerReject( reason, session.Step, session.Bank );
		}
	}

	private RiskLadderSession GetSession( long steamId )
	{
		if ( _sessionsBySteamId.TryGetValue( steamId, out var session ) )
			return session;

		return default;
	}

	public int GetBankForStep( int step )
	{
		var safeStep = Math.Clamp( step, 1, GetSafeMaxStep() );
		var index = safeStep - 1;
		var multiplier = FirstWinMultiplier + MultiplierStep * index + MultiplierCurve * index * index;
		multiplier = Math.Clamp( multiplier, 0.01f, MathF.Max( 0.01f, MaxBankMultiplier ) );

		return RoundMoney( BetCost * multiplier );
	}

	public float GetWinChanceForStep( int step )
	{
		var safeStep = Math.Clamp( step, 1, GetSafeMaxStep() );
		var chance = StartWinChance - ChanceDropPerStep * (safeStep - 1);
		return Math.Clamp( chance, Math.Clamp( MinWinChance, 0.01f, 0.99f ), 0.99f );
	}

	private int RoundMoney( float value )
	{
		var roundTo = Math.Max( 1, BankRoundTo );
		var rounded = MathF.Round( value / roundTo ) * roundTo;
		return Math.Max( 1, (int)rounded );
	}

	private int GetSafeMaxStep()
	{
		return Math.Max( 1, MaxStep );
	}

	private bool CanPlayerUseMachine( Player player )
	{
		var distance = Vector3.DistanceBetween( player.WorldPosition, WorldPosition );
		return distance <= MaxDistance;
	}

	private bool IsOnCooldown( long steamId )
	{
		return _nextActionTimeBySteamId.TryGetValue( steamId, out var nextAllowedTime )
			&& Time.Now < nextAllowedTime;
	}

	private void SetCooldown( long steamId, float seconds )
	{
		_nextActionTimeBySteamId[steamId] = Time.Now + MathF.Max( 0.05f, seconds );
	}

	private static bool TryGetPlayerFromPress( IPressable.Event e, out Player player )
	{
		player = null;

		var source = e.Source?.GameObject;
		if ( !source.IsValid() )
			return false;

		return source.Components.TryGet( out player, FindMode.EverythingInSelfAndParent );
	}

	protected override void OnStart()
	{
		_localStep = 0;
		_localBank = 0;
		_localStatusText = GameLocalization.Phrase( "ui.common.ready", "Ready" );
		_localVisualState = "idle";
	}

	protected override void OnUpdate()
	{
		HandleLocalCashoutInput();

		if ( !_pendingReveal )
			return;

		if ( !_pendingRevealTimer )
			return;

		_pendingReveal = false;

		if ( _pendingWin )
		{
			_localStep = _pendingStep;
			_localBank = _pendingBank;
			_localVisualState = "win";
			_localStatusText = GameLocalization.Format( "ui.casino.bank_money", "Bank ${0}", _localBank );
			Notification.Info( GameLocalization.Format( "notify.casino.risk_win", "Risk ladder win: bank ${0} ({1:P0})", _localBank, _pendingWinChance ), 2.2f );
			return;
		}

		_localStep = 0;
		_localBank = 0;
		_localVisualState = "lose";
		_localStatusText = _pendingStartedRound
			? GameLocalization.Format( "ui.casino.lose_money", "Lose -${0}", BetCost )
			: GameLocalization.Format( "ui.casino.bust_money", "Bust -${0}", _pendingLostValue );

		Notification.Error( _pendingStartedRound
			? GameLocalization.Format( "notify.casino.risk_lose", "Risk ladder lose: -${0}", BetCost )
			: GameLocalization.Format( "notify.casino.risk_bust", "Risk ladder bust: -${0}", _pendingLostValue ), 2.2f );
	}

	private void HandleLocalCashoutInput()
	{
		if ( string.IsNullOrWhiteSpace( CashoutInput ) )
			return;

		if ( !Input.Pressed( CashoutInput ) )
			return;

		if ( _localBank <= 0 )
			return;

		var player = Player.Local;
		if ( !player.IsValid() || player.IsProxy || !player.Controller.IsValid() )
			return;

		if ( !IsLocalPlayerFacingMachine( player ) )
			return;

		if ( Networking.IsHost )
		{
			HostCashout( player, player.GameObject.Network.Owner );
			return;
		}

		RpcRequestCashout( GameObject );
	}

	private bool IsLocalPlayerFacingMachine( Player player )
	{
		var eye = player.Controller.EyeTransform;
		var targetPosition = WorldPosition + Vector3.Up * 35f;
		var toMachine = targetPosition - eye.Position;

		if ( toMachine.Length > MaxDistance + 40f )
			return false;

		if ( toMachine.LengthSquared <= 0.001f )
			return true;

		var dot = Vector3.Dot( eye.Forward.Normal, toMachine.Normal );
		return dot >= Math.Clamp( CashoutLookDot, -1f, 1f );
	}

	private struct RiskLadderSession
	{
		public int Step;
		public int Bank;
		public bool IsSpinning;
		public bool HasBank => Bank > 0 && Step > 0;
	}
}
