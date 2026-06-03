using Sandbox;
using System;
using System.Collections.Generic;

public sealed partial class Roulette : Component, Component.IPressable
{
	public const int MaxBetsPerPlayer = 3;
	public const int StateBetting = 0;
	public const int StateSpinning = 1;
	public const int StateResult = 2;

	private static readonly int[] RedNumbers =
	{
		1, 3, 5, 7, 9, 12, 14, 16, 18,
		19, 21, 23, 25, 27, 30, 32, 34, 36
	};

	[Property, Group( "Gameplay" )] public int MinBet { get; set; } = 10;
	[Property, Group( "Gameplay" )] public int MaxBet { get; set; } = 1000;
	[Property, Group( "Gameplay" )] public float BettingIntervalSeconds { get; set; } = 30f;
	[Property, Group( "Gameplay" )] public float SpinDurationSeconds { get; set; } = 5f;
	[Property, Group( "Gameplay" )] public float ResultHoldSeconds { get; set; } = 1.5f;
	[Property, Group( "Gameplay" )] public float MaxUseDistance { get; set; } = 120f;
	[Property, Group( "Visual" )] public GameObject Circle { get; set; }
	[Property, Group( "Visual" )] public GameObject RouletteWorldText { get; set; }
	[Property, Group( "Visual" )] public float CircleSpinYawSpeed { get; set; } = 720f;

	[Property, Group( "Payouts" )] public int ZeroMultiplier { get; set; } = 36;
	[Property, Group( "Payouts" )] public int StraightMultiplier { get; set; } = 36;
	[Property, Group( "Payouts" )] public int EvenOddMultiplier { get; set; } = 2;
	[Property, Group( "Payouts" )] public int DozenMultiplier { get; set; } = 3;
	[Property, Group( "Payouts" )] public int LowHighMultiplier { get; set; } = 2;
	[Property, Group( "Payouts" )] public int ColumnMultiplier { get; set; } = 3;
	[Property, Group( "Payouts" )] public int ColorMultiplier { get; set; } = 2;

	[Sync( SyncFlags.FromHost )]
	[Change( nameof( OnRoundStateSynced ) )]
	public int RoundState { get; private set; } = StateBetting;

	[Sync( SyncFlags.FromHost )]
	[Change( nameof( OnCurrentDisplayNumberSynced ) )]
	public int CurrentDisplayNumber { get; private set; }

	[Sync( SyncFlags.FromHost )]
	[Change( nameof( OnFinalNumberSynced ) )]
	public int FinalNumber { get; private set; } = -1;
	// Host-only authority timers (not synced).
	private TimeUntil BettingTimeUntil { get; set; }
	private TimeUntil SpinTimeUntil { get; set; }
	private TimeUntil ResultTimeUntil { get; set; }

	// Client display — seeded by RpcBroadcastRoundPhase (Sync RoundState alone is not enough on proxies).
	public TimeUntil ClientPhaseTimeUntil { get; private set; }
	public int ClientPhaseRoundState { get; private set; } = StateBetting;
	public int ClientDisplayNumber { get; private set; }
	public int ClientFinalNumber { get; private set; } = -1;

	public bool IsBetting => RoundState == StateBetting;
	public bool IsSpinning => RoundState == StateSpinning;
	public bool IsShowingResult => RoundState == StateResult;

	/// <summary>Round phase for world UI / client visuals (from host broadcast).</summary>
	public int UiRoundState => ClientPhaseRoundState;

	public bool IsUiBetting => UiRoundState == StateBetting;
	public bool IsUiSpinning => UiRoundState == StateSpinning;
	public bool IsUiShowingResult => UiRoundState == StateResult;

	public int UiDisplayNumber => IsUiSpinning ? ClientDisplayNumber : CurrentDisplayNumber;
	public int UiFinalNumber => ClientFinalNumber >= 0 ? ClientFinalNumber : FinalNumber;

	public int GetPhaseSecondsLeft() => Math.Max( 0, (int)Math.Ceiling( (float)ClientPhaseTimeUntil ) );
	public IReadOnlyList<RouletteBet> LocalBets => _localBets;
	public int LocalBetCount => _localBets.Count;
	public static Roulette LocalActiveRoulette { get; private set; }

	private readonly List<RouletteBet> _localBets = new();
	private Rotation _circleBaseRotation = Rotation.Identity;
	private bool _circleBaseRotationCached;

	[Sync( SyncFlags.FromHost )] private float SyncCircleSpinYaw { get; set; }
	private float _clientCircleVisualYaw;

	public bool Press( IPressable.Event e )
	{
		if ( !TryGetPlayerFromPress( e, out var player ) )
			return false;

		if ( player.IsProxy )
			return false;

		RoulettePanel.Open( this );
		return true;
	}

	public void RequestPlaceBet( int kind, int target, int amount )
	{
		if ( Networking.IsHost )
		{
#if SERVER
			var local = Player.Local;
			if ( local.IsValid() )
				HostPlaceBet( local, local.GameObject.Network.Owner, kind, target, amount );
#endif

			return;
		}

		RpcRequestPlaceBet( GameObject, kind, target, amount );
	}

	public int GetLocalBetTotal()
	{
		var total = 0;
		foreach ( var bet in _localBets )
			total += Math.Max( 0, bet.Amount );

		return total;
	}

	public static bool HasLocalActiveRouletteOtherThan( Roulette roulette )
	{
		if ( !LocalActiveRoulette.IsValid() )
		{
			LocalActiveRoulette = null;
			return false;
		}

		return LocalActiveRoulette != roulette && LocalActiveRoulette.LocalBetCount > 0;
	}

	public static bool IsRedNumber( int number )
	{
		for ( var i = 0; i < RedNumbers.Length; i++ )
		{
			if ( RedNumbers[i] == number )
				return true;
		}

		return false;
	}

	public string GetBetLabel( RouletteBet bet )
	{
		return GetBetLabel( bet.Kind, bet.Target );
	}

	public string GetBetLabel( int kind, int target )
	{
		return (RouletteBetKind)kind switch
		{
			RouletteBetKind.Zero => "0",
			RouletteBetKind.Straight => target.ToString(),
			RouletteBetKind.OddEven => target == 0
				? GameLocalization.Phrase( "ui.roulette.even", "Even" )
				: GameLocalization.Phrase( "ui.roulette.odd", "Odd" ),
			RouletteBetKind.Dozen => target switch
			{
				1 => GameLocalization.Phrase( "ui.roulette.first_12", "1st 12" ),
				2 => GameLocalization.Phrase( "ui.roulette.second_12", "2nd 12" ),
				_ => GameLocalization.Phrase( "ui.roulette.third_12", "3rd 12" )
			},
			RouletteBetKind.LowHigh => target == 0
				? GameLocalization.Phrase( "ui.roulette.low", "1-18" )
				: GameLocalization.Phrase( "ui.roulette.high", "19-36" ),
			RouletteBetKind.Column => GameLocalization.Phrase( "ui.roulette.column", "2 to 1" ),
			RouletteBetKind.Color => target == 0
				? GameLocalization.Phrase( "ui.roulette.black", "Black" )
				: GameLocalization.Phrase( "ui.roulette.red", "Red" ),
			_ => GameLocalization.Phrase( "ui.roulette.bet", "Bet" )
		};
	}

	public int GetMultiplierForBet( int kind )
	{
		return (RouletteBetKind)kind switch
		{
			RouletteBetKind.Zero => Math.Max( 1, ZeroMultiplier ),
			RouletteBetKind.Straight => Math.Max( 1, StraightMultiplier ),
			RouletteBetKind.OddEven => Math.Max( 1, EvenOddMultiplier ),
			RouletteBetKind.Dozen => Math.Max( 1, DozenMultiplier ),
			RouletteBetKind.LowHigh => Math.Max( 1, LowHighMultiplier ),
			RouletteBetKind.Column => Math.Max( 1, ColumnMultiplier ),
			RouletteBetKind.Color => Math.Max( 1, ColorMultiplier ),
			_ => 1
		};
	}

	[Rpc.Host]
	private void RpcRequestPlaceBet( GameObject rouletteGo, int kind, int target, int amount )
	{
#if SERVER
		RpcRequestPlaceBetServer( rouletteGo, kind, target, amount );
#endif
	}

	[Rpc.Broadcast]
	private void RpcOwnerBetAccepted( int kind, int target, int amount, int multiplier )
	{
		if ( LocalActiveRoulette.IsValid() && LocalActiveRoulette != this )
			LocalActiveRoulette._localBets.Clear();

		LocalActiveRoulette = this;
		_localBets.Add( new RouletteBet( kind, target, amount, multiplier ) );

		var label = GetBetLabel( kind, target );
		Notification.Info( GameLocalization.Format( "notify.roulette.bet_placed", "Roulette bet placed: {0}, ${1}.", label, amount ), 3.5f );
		RoulettePanel.NotifyBetAccepted( this );
	}

	[Rpc.Broadcast]
	private void RpcOwnerBetRejected( string reason )
	{
		var text = string.IsNullOrWhiteSpace( reason )
			? GameLocalization.Phrase( "notify.casino.invalid_bet", "Invalid bet" )
			: reason;

		Notification.Error( text, 3.5f );
		RoulettePanel.NotifyBetRejected( this, text );
	}

	[Rpc.Broadcast]
	private void RpcBroadcastSpinDisplay( int displayNumber )
	{
		if ( ClientPhaseRoundState == StateSpinning )
			ClientDisplayNumber = displayNumber;
	}

	[Rpc.Broadcast]
	private void RpcBroadcastRoundPhase( int roundState, float phaseDurationSeconds, int displayNumber, int finalNumber )
	{
		ClientPhaseRoundState = roundState;
		ClientPhaseTimeUntil = MathF.Max( 0f, phaseDurationSeconds );
		ClientDisplayNumber = displayNumber;

		if ( finalNumber >= 0 )
			ClientFinalNumber = finalNumber;

		if ( roundState == StateBetting || roundState == StateSpinning )
			_clientCircleVisualYaw = 0f;
	}

	private void OnRoundStateSynced( int oldState, int newState )
	{
		if ( ClientPhaseTimeUntil <= 0f )
			ClientPhaseRoundState = newState;
	}

	private void OnCurrentDisplayNumberSynced( int oldValue, int newValue )
	{
		if ( IsUiSpinning )
			ClientDisplayNumber = newValue;
	}

	private void OnFinalNumberSynced( int oldValue, int newValue )
	{
		if ( newValue >= 0 )
			ClientFinalNumber = newValue;
	}

	[Rpc.Broadcast]
	private void RpcOwnerRoundResult( int finalNumber, int totalBet, int payout )
	{
		_localBets.Clear();

		if ( LocalActiveRoulette == this )
			LocalActiveRoulette = null;

		RoulettePanel.NotifyRoundResolved( this );

		if ( payout > 0 )
		{
			Notification.Info( GameLocalization.Format( "notify.roulette.win", "Roulette {0}: +${1}.", finalNumber, payout ), 4f );
			return;
		}

		Notification.Error( GameLocalization.Format( "notify.roulette.lose", "Roulette {0}: -${1}.", finalNumber, totalBet ), 4f );
	}

	protected override void OnStart()
	{
		CacheCircleBaseRotation();

#if SERVER
		if ( Networking.IsHost )
			HostStartBettingRound();
#endif
	}

	protected override void OnUpdate()
	{
#if SERVER
		HostUpdateCircleRotation();

		if ( !Networking.IsHost )
			return;

		if ( !_hostInitialized )
			HostStartBettingRound();

		CleanupDisconnectedPlayers();
		HostUpdateRound();
#endif

		ApplyCircleRotation();
	}

	protected override void OnDestroy()
	{
#if SERVER
		ReleaseAllPlayerLocks();
#endif

		if ( LocalActiveRoulette == this )
			LocalActiveRoulette = null;

		RoulettePanel.CloseForRoulette( this );
	}

	private void CacheCircleBaseRotation()
	{
		if ( Circle.IsValid() )
		{
			_circleBaseRotation = Circle.LocalRotation;
			_circleBaseRotationCached = true;
		}
	}

	private void ApplyCircleRotation()
	{
		if ( !Circle.IsValid() )
			return;

		if ( !_circleBaseRotationCached )
			CacheCircleBaseRotation();

		if ( IsUiSpinning )
			_clientCircleVisualYaw = Angles.NormalizeAngle( _clientCircleVisualYaw + CircleSpinYawSpeed * Time.Delta );

		var yaw = IsUiSpinning ? _clientCircleVisualYaw : SyncCircleSpinYaw;
		Circle.LocalRotation = _circleBaseRotation * Rotation.FromYaw( yaw );
	}

	private static bool TryGetPlayerFromPress( IPressable.Event e, out Player player )
	{
		player = null;

		var source = e.Source?.GameObject;
		if ( !source.IsValid() )
			return false;

		return source.Components.TryGet( out player, FindMode.EverythingInSelfAndParent );
	}

	public readonly struct RouletteBet
	{
		public int Kind { get; }
		public int Target { get; }
		public int Amount { get; }
		public int Multiplier { get; }

		public RouletteBet( int kind, int target, int amount, int multiplier )
		{
			Kind = kind;
			Target = target;
			Amount = amount;
			Multiplier = multiplier;
		}
	}
}

public enum RouletteBetKind
{
	Zero = 0,
	Straight = 1,
	OddEven = 2,
	Dozen = 3,
	LowHigh = 4,
	Column = 5,
	Color = 6
}
