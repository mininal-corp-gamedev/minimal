using Sandbox;
using System;
using System.Collections.Generic;

public sealed partial class Roulette
{
	private const float DisplayNumberStepSeconds = 0.5f;

	private static readonly Dictionary<long, Roulette> ActiveRouletteBySteamId = new();

	private readonly Dictionary<long, List<RouletteBet>> _betsBySteamId = new();
	private readonly List<long> _cleanupScratch = new();
	private bool _hostInitialized;
	private TimeUntil _nextDisplayNumberUpdate;

	private void RpcRequestPlaceBetServer( GameObject rouletteGo, int kind, int target, int amount )
	{
		if ( !Networking.IsHost )
			return;

		if ( !rouletteGo.IsValid() )
			return;

		if ( rouletteGo != GameObject )
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

		var isFirstBet = bets.Count == 0;
		bets.Add( bet );
		ActiveRouletteBySteamId[steamId] = this;
		player.Money -= amount;

		if ( isFirstBet )
			player.HostGrantAchievement( "roulette" );

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

	private void SendBetRejectedToOwner( Connection connection, string reason )
	{
		if ( connection is null )
			return;

		using ( Rpc.FilterInclude( c => c.SteamId.Value == connection.SteamId.Value ) )
		{
			RpcOwnerBetRejected( reason );
		}
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
				RpcBroadcastSpinDisplay( CurrentDisplayNumber );
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
		SyncCircleSpinYaw = 0f;
		var duration = MathF.Max( 1f, BettingIntervalSeconds );
		BettingTimeUntil = duration;
		SpinTimeUntil = 0f;
		ResultTimeUntil = 0f;
		BroadcastCurrentPhase();
	}

	private void HostUpdateCircleRotation()
	{
		if ( !Circle.IsValid() )
			return;

		if ( !_circleBaseRotationCached )
			CacheCircleBaseRotation();

		if ( IsSpinning )
			SyncCircleSpinYaw = Angles.NormalizeAngle( SyncCircleSpinYaw + CircleSpinYawSpeed * Time.Delta );
	}

	private float GetHostPhaseRemainingSeconds()
	{
		if ( IsBetting )
			return MathF.Max( 0f, (float)BettingTimeUntil );

		if ( IsSpinning )
			return MathF.Max( 0f, (float)SpinTimeUntil );

		if ( IsShowingResult )
			return MathF.Max( 0f, (float)ResultTimeUntil );

		return 0f;
	}

	private void BroadcastCurrentPhase()
	{
		var remaining = GetHostPhaseRemainingSeconds();
		RpcBroadcastRoundPhase( RoundState, remaining, CurrentDisplayNumber, FinalNumber );
	}

	public static void HostSyncAllToConnection( Connection connection )
	{
		if ( !Networking.IsHost || connection is null )
			return;

		var scene = Game.ActiveScene;
		if ( scene is null )
			return;

		foreach ( var roulette in scene.GetAllComponents<Roulette>() )
		{
			if ( !roulette.IsValid() )
				continue;

			roulette.HostSyncPhaseToConnection( connection );
		}
	}

	private void HostSyncPhaseToConnection( Connection connection )
	{
		if ( !Networking.IsHost || connection is null )
			return;

		var remaining = GetHostPhaseRemainingSeconds();
		using ( Rpc.FilterInclude( c => c.SteamId.Value == connection.SteamId.Value ) )
		{
			RpcBroadcastRoundPhase( RoundState, remaining, CurrentDisplayNumber, FinalNumber );
		}
	}

	private void HostStartSpin()
	{
		RoundState = StateSpinning;
		var duration = MathF.Max( 0.1f, SpinDurationSeconds );
		SpinTimeUntil = duration;
		BettingTimeUntil = 0f;
		ResultTimeUntil = 0f;
		CurrentDisplayNumber = Game.Random.Int( 0, 36 );
		_nextDisplayNumberUpdate = DisplayNumberStepSeconds;
		BroadcastCurrentPhase();
	}

	private void HostFinishSpin()
	{
		RoundState = StateResult;
		FinalNumber = Game.Random.Int( 0, 36 );
		CurrentDisplayNumber = FinalNumber;
		var duration = MathF.Max( 0.1f, ResultHoldSeconds );
		ResultTimeUntil = duration;
		SpinTimeUntil = 0f;
		BroadcastCurrentPhase();

		ResolveBetsOnHost();
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

			if ( player.IsValid() )
				TryAdvanceFirstQuest( player );

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

	private static void TryAdvanceFirstQuest( Player player )
	{
		if ( !player.IsValid() ) return;

		var pq = player.Components.Get<PlayerQuest>();
		if ( !pq.IsValid() ) return;

		var def = QuestDatabase.FindQuestById( "q1" );
		if ( def == null ) return;

		var active = pq.GetActiveQuest( def );
		if ( active == null ) return;
		if ( active.CurrentQuestTask == null || active.CurrentQuestTask.Id != "q1t1" ) return;

		QuestManager.Instance?.AdvanceCount( pq, def );
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

	private void ReleaseAllPlayerLocks()
	{
		foreach ( var steamId in _betsBySteamId.Keys )
			ReleasePlayerLock( steamId, this );

		_betsBySteamId.Clear();
	}

	private static void ReleasePlayerLock( long steamId, Roulette roulette )
	{
		if ( ActiveRouletteBySteamId.TryGetValue( steamId, out var active ) && active == roulette )
			ActiveRouletteBySteamId.Remove( steamId );
	}
}
