using Sandbox;
using System;
using System.Collections.Generic;

public sealed class Roulette : Component, Component.IPressable
{
	public const int MaxBetsPerPlayer = 3;
	public const int StateBetting = 0;
	public const int StateSpinning = 1;
	public const int StateResult = 2;

	private const float DisplayNumberStepSeconds = 0.5f;

	private static readonly int[] RedNumbers =
	{
		1, 3, 5, 7, 9, 12, 14, 16, 18,
		19, 21, 23, 25, 27, 30, 32, 34, 36
	};

	private static readonly Dictionary<long, Roulette> ActiveRouletteBySteamId = new();

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

	[Sync( SyncFlags.FromHost )] public int RoundState { get; private set; } = StateBetting;
	[Sync( SyncFlags.FromHost )] public int CurrentDisplayNumber { get; private set; }
	[Sync( SyncFlags.FromHost )] public int FinalNumber { get; private set; } = -1;
	[Sync( SyncFlags.FromHost )] public TimeUntil BettingTimeUntil { get; private set; }
	[Sync( SyncFlags.FromHost )] public TimeUntil SpinTimeUntil { get; private set; }
	[Sync( SyncFlags.FromHost )] public TimeUntil ResultTimeUntil { get; private set; }

	public bool IsBetting => RoundState == StateBetting;
	public bool IsSpinning => RoundState == StateSpinning;
	public bool IsShowingResult => RoundState == StateResult;
	public IReadOnlyList<RouletteBet> LocalBets => _localBets;
	public int LocalBetCount => _localBets.Count;
	public static Roulette LocalActiveRoulette { get; private set; }

	private readonly Dictionary<long, List<RouletteBet>> _betsBySteamId = new();
	private readonly List<RouletteBet> _localBets = new();
	private bool _hostInitialized;
	private TimeUntil _nextDisplayNumberUpdate;
	private Rotation _circleBaseRotation = Rotation.Identity;
	private float _circleSpinYaw;
	private bool _circleBaseRotationCached;

	public bool Press( IPressable.Event e )
	{
		if ( !TryGetPlayerFromPress( e, out var player ) )
			return false;

		if ( player.IsProxy )
			return false;

		if ( IsSpinning || IsShowingResult )
		{
			RoulettePanel.CloseForRoulette( this );
			Notification.Error( GameLocalization.Phrase( "notify.roulette.spinning", "Roulette is spinning." ), 3.5f );
			return false;
		}

		RoulettePanel.Open( this );
		return true;
	}

	public void RequestPlaceBet( int kind, int target, int amount )
	{
		if ( Networking.IsHost )
		{
			var local = Player.Local;
			if ( local.IsValid() )
				HostPlaceBet( local, local.GameObject.Network.Owner, kind, target, amount );

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
		if ( !Networking.IsHost )
			return;

		if ( !rouletteGo.IsValid() )
			return;

		var roulette = rouletteGo.Components.Get<Roulette>();
		if ( !roulette.IsValid() )
			return;

		var caller = Rpc.Caller;
		if ( caller is null )
			return;

		var player = Player.FindPlayerBySteamId( caller.SteamId.Value );
		if ( !player.IsValid() || player.GameObject.Network.Owner != caller )
		{
			roulette.SendBetRejectedToOwner( caller, GameLocalization.Phrase( "notify.player.not_ready", "Your player is not ready." ) );
			return;
		}

		roulette.HostPlaceBet( player, caller, kind, target, amount );
	}

	private void HostPlaceBet( Player player, Connection connection, int kind, int target, int amount )
	{
		if ( !Networking.IsHost || !player.IsValid() || connection is null )
			return;

		if ( !ValidateBetOnHost( player, connection, kind, target, amount, out var reason ) )
		{
			SendBetRejectedToOwner( connection, reason );
			return;
		}

		var steamId = connection.SteamId.Value;
		var bet = new RouletteBet( kind, target, amount, GetMultiplierForBet( kind ) );

		if ( !_betsBySteamId.TryGetValue( steamId, out var bets ) )
		{
			bets = new List<RouletteBet>();
			_betsBySteamId[steamId] = bets;
		}

		bets.Add( bet );
		ActiveRouletteBySteamId[steamId] = this;
		player.Money -= amount;

		using ( Rpc.FilterInclude( c => c.SteamId.Value == steamId ) )
		{
			RpcOwnerBetAccepted( kind, target, amount, bet.Multiplier );
		}
	}

	private bool ValidateBetOnHost( Player player, Connection connection, int kind, int target, int amount, out string reason )
	{
		reason = "";

		if ( IsSpinning || IsShowingResult )
		{
			reason = GameLocalization.Phrase( "notify.roulette.spinning", "Roulette is spinning." );
			return false;
		}

		if ( !player.IsAlive )
		{
			reason = GameLocalization.Phrase( "notify.player.not_ready", "Your player is not ready." );
			return false;
		}

		if ( Vector3.DistanceBetween( player.WorldPosition, WorldPosition ) > MathF.Max( 1f, MaxUseDistance ) )
		{
			reason = GameLocalization.Phrase( "notify.casino.too_far", "Too far" );
			return false;
		}

		var minBet = Math.Max( 1, MinBet );
		var maxBet = Math.Max( minBet, MaxBet );
		if ( amount < minBet || amount > maxBet )
		{
			reason = GameLocalization.Format( "notify.roulette.min_max", "Bet must be ${0}-${1}.", minBet, maxBet );
			return false;
		}

		if ( !IsValidBetTarget( kind, target ) )
		{
			reason = GameLocalization.Phrase( "notify.roulette.invalid_selection", "Choose a valid roulette field." );
			return false;
		}

		var steamId = connection.SteamId.Value;
		if ( ActiveRouletteBySteamId.TryGetValue( steamId, out var activeRoulette ) && activeRoulette.IsValid() && activeRoulette != this )
		{
			reason = GameLocalization.Phrase( "notify.roulette.other_active", "You already have bets on another roulette." );
			return false;
		}

		if ( GetBetCount( steamId ) >= MaxBetsPerPlayer )
		{
			reason = GameLocalization.Phrase( "notify.roulette.max_bets", "Maximum roulette bets reached." );
			return false;
		}

		if ( player.Money < amount )
		{
			reason = GameLocalization.Phrase( "ui.shop.not_enough_money", "Not enough money" );
			return false;
		}

		return true;
	}

	private int GetBetCount( long steamId )
	{
		return _betsBySteamId.TryGetValue( steamId, out var bets ) ? bets.Count : 0;
	}

	private bool IsValidBetTarget( int kind, int target )
	{
		return (RouletteBetKind)kind switch
		{
			RouletteBetKind.Zero => target == 0,
			RouletteBetKind.Straight => target >= 1 && target <= 36,
			RouletteBetKind.OddEven => target is 0 or 1,
			RouletteBetKind.Dozen => target >= 1 && target <= 3,
			RouletteBetKind.LowHigh => target is 0 or 1,
			RouletteBetKind.Column => target >= 1 && target <= 3,
			RouletteBetKind.Color => target is 0 or 1,
			_ => false
		};
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

	private void SendBetRejectedToOwner( Connection connection, string reason )
	{
		if ( connection is null )
			return;

		using ( Rpc.FilterInclude( c => c.SteamId.Value == connection.SteamId.Value ) )
		{
			RpcOwnerBetRejected( reason );
		}
	}

	[Rpc.Broadcast]
	private void RpcClosePanelForSpin( GameObject rouletteGo )
	{
		if ( !rouletteGo.IsValid() )
			return;

		var roulette = rouletteGo.Components.Get<Roulette>();
		if ( roulette.IsValid() )
			RoulettePanel.CloseForRoulette( roulette );
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

		if ( Networking.IsHost )
			HostStartBettingRound();
	}

	protected override void OnUpdate()
	{
		UpdateCircleRotation();

		if ( !Networking.IsHost )
			return;

		if ( !_hostInitialized )
			HostStartBettingRound();

		CleanupDisconnectedPlayers();
		HostUpdateRound();
	}

	protected override void OnDestroy()
	{
		foreach ( var steamId in _betsBySteamId.Keys )
			ReleasePlayerLock( steamId, this );

		_betsBySteamId.Clear();

		if ( LocalActiveRoulette == this )
			LocalActiveRoulette = null;

		RoulettePanel.CloseForRoulette( this );
	}

	private void HostUpdateRound()
	{
		if ( IsBetting && BettingTimeUntil )
		{
			HostStartSpin();
			return;
		}

		if ( IsSpinning )
		{
			if ( _nextDisplayNumberUpdate )
			{
				CurrentDisplayNumber = Game.Random.Int( 0, 36 );
				_nextDisplayNumberUpdate = DisplayNumberStepSeconds;
			}

			if ( SpinTimeUntil )
				HostFinishSpin();

			return;
		}

		if ( IsShowingResult && ResultTimeUntil )
			HostStartBettingRound();
	}

	private void HostStartBettingRound()
	{
		_hostInitialized = true;
		RoundState = StateBetting;
		BettingTimeUntil = MathF.Max( 1f, BettingIntervalSeconds );
		SpinTimeUntil = 0f;
		ResultTimeUntil = 0f;
	}

	private void HostStartSpin()
	{
		RoundState = StateSpinning;
		SpinTimeUntil = MathF.Max( 0.1f, SpinDurationSeconds );
		BettingTimeUntil = 0f;
		ResultTimeUntil = 0f;
		CurrentDisplayNumber = Game.Random.Int( 0, 36 );
		_nextDisplayNumberUpdate = DisplayNumberStepSeconds;
		RpcClosePanelForSpin( GameObject );
	}

	private void HostFinishSpin()
	{
		RoundState = StateResult;
		FinalNumber = Game.Random.Int( 0, 36 );
		CurrentDisplayNumber = FinalNumber;
		ResultTimeUntil = MathF.Max( 0.1f, ResultHoldSeconds );
		SpinTimeUntil = 0f;

		ResolveBetsOnHost();
	}

	private void CacheCircleBaseRotation()
	{
		if ( Circle.IsValid() )
		{
			_circleBaseRotation = Circle.LocalRotation;
			_circleBaseRotationCached = true;
		}
	}

	private void UpdateCircleRotation()
	{
		if ( !Circle.IsValid() )
			return;

		if ( !_circleBaseRotationCached )
			CacheCircleBaseRotation();

		if ( IsSpinning )
			_circleSpinYaw = Angles.NormalizeAngle( _circleSpinYaw + CircleSpinYawSpeed * Time.Delta );

		Circle.LocalRotation = _circleBaseRotation * Rotation.FromYaw( _circleSpinYaw );
	}

	private void ResolveBetsOnHost()
	{
		foreach ( var pair in _betsBySteamId )
		{
			var steamId = pair.Key;
			var player = Player.FindPlayerBySteamId( steamId );
			var connection = player.IsValid() ? player.GameObject.Network.Owner : null;
			var totalBet = 0;
			var payout = 0;

			foreach ( var bet in pair.Value )
			{
				totalBet += Math.Max( 0, bet.Amount );

				if ( DoesBetWin( bet, FinalNumber ) )
					payout += Math.Max( 0, bet.Amount * Math.Max( 1, bet.Multiplier ) );
			}

			if ( player.IsValid() && payout > 0 )
				player.Money += payout;

			if ( connection is not null )
			{
				using ( Rpc.FilterInclude( c => c.SteamId.Value == steamId ) )
				{
					RpcOwnerRoundResult( FinalNumber, totalBet, payout );
				}
			}

			ReleasePlayerLock( steamId, this );
		}

		_betsBySteamId.Clear();
	}

	private bool DoesBetWin( RouletteBet bet, int number )
	{
		return (RouletteBetKind)bet.Kind switch
		{
			RouletteBetKind.Zero => number == 0,
			RouletteBetKind.Straight => number == bet.Target,
			RouletteBetKind.OddEven => number > 0 && (bet.Target == 0 ? number % 2 == 0 : number % 2 != 0),
			RouletteBetKind.Dozen => number > 0 && ((number - 1) / 12) + 1 == bet.Target,
			RouletteBetKind.LowHigh => bet.Target == 0 ? number >= 1 && number <= 18 : number >= 19 && number <= 36,
			RouletteBetKind.Column => number > 0 && ((number - 1) % 3) + 1 == bet.Target,
			RouletteBetKind.Color => number > 0 && (bet.Target == 1 ? IsRedNumber( number ) : !IsRedNumber( number )),
			_ => false
		};
	}

	private void CleanupDisconnectedPlayers()
	{
		if ( _betsBySteamId.Count <= 0 )
			return;

		_cleanupScratch.Clear();
		foreach ( var steamId in _betsBySteamId.Keys )
		{
			var player = Player.FindPlayerBySteamId( steamId );
			if ( !player.IsValid() || player.GameObject.Network.Owner is null )
				_cleanupScratch.Add( steamId );
		}

		foreach ( var steamId in _cleanupScratch )
		{
			_betsBySteamId.Remove( steamId );
			ReleasePlayerLock( steamId, this );
		}
	}

	private readonly List<long> _cleanupScratch = new();

	private static void ReleasePlayerLock( long steamId, Roulette roulette )
	{
		if ( ActiveRouletteBySteamId.TryGetValue( steamId, out var active ) && active == roulette )
			ActiveRouletteBySteamId.Remove( steamId );
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
