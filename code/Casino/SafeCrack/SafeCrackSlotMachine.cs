using Sandbox;
using System;
using System.Collections.Generic;

public sealed class SafeCrackSlotMachine : Component, Component.IPressable
{
	private const int RequiredLocks = 3;
	private const float MinRevealDelay = 0.1f;

	[Property, Group( "Gameplay" )] public int BetCost { get; set; } = 50;
	[Property, Group( "Gameplay" )] public float OpenChance { get; set; } = 0.45f;
	[Property, Group( "Gameplay" )] public float OpenChanceDropPerLock { get; set; } = 0.06f;
	[Property, Group( "Gameplay" )] public float MinOpenChance { get; set; } = 0.25f;
	[Property, Group( "Gameplay" )] public float SmallPayoutChance { get; set; } = 0.18f;
	[Property, Group( "Gameplay" )] public float SmallPayoutMultiplier { get; set; } = 1.4f;
	[Property, Group( "Gameplay" )] public float JackpotMultiplier { get; set; } = 12f;
	[Property, Group( "Gameplay" )] public int PayoutRoundTo { get; set; } = 5;
	[Property, Group( "Gameplay" )] public float MaxDistance { get; set; } = 120f;
	[Property, Group( "Gameplay" )] public float ActionCooldownSeconds { get; set; } = 0.35f;
	[Property, Group( "Visual" )] public float SpinDuration { get; set; } = 0.85f;

	public int LocalLocksOpened => _localLocksOpened;
	public int LocalRequiredLocks => RequiredLocks;
	public int LocalJackpotPayout => GetJackpotPayout();
	public int LocalSmallPayout => GetSmallPayout();
	public float LocalNextOpenChance => GetOpenChanceForLock( _localLocksOpened );
	public string LocalStatusText => _localStatusText;
	public string LocalVisualState => _localVisualState;

	private readonly Dictionary<long, SafeCrackSession> _sessionsBySteamId = new();
	private readonly Dictionary<long, float> _nextActionTimeBySteamId = new();

	private int _localLocksOpened;
	private string _localStatusText = GameLocalization.Phrase( "ui.common.ready", "Ready" );
	private string _localVisualState = "idle";

	private bool _pendingReveal;
	private string _pendingOutcome = "idle";
	private int _pendingLocksOpened;
	private int _pendingPayout;
	private int _pendingLostValue;
	private TimeUntil _pendingRevealTimer;

	public bool Press( IPressable.Event e )
	{
		if ( !TryGetPlayerFromPress( e, out var player ) )
			return false;

		if ( player.IsProxy )
			return false;

		if ( Networking.IsHost )
		{
			HostSpin( player, player.GameObject.Network.Owner );
			return true;
		}

		RpcRequestSpin( GameObject );
		return true;
	}

	[Rpc.Host]
	private void RpcRequestSpin( GameObject slotMachineGo )
	{
		if ( !Networking.IsHost )
			return;

		if ( slotMachineGo != GameObject )
			return;

		var slotMachine = slotMachineGo.Components.Get<SafeCrackSlotMachine>();
		if ( !slotMachine.IsValid() )
			return;

		var caller = Rpc.Caller;
		if ( caller is null )
			return;

		var player = Player.FindPlayerBySteamId( caller.SteamId.Value );
		if ( !player.IsValid() || player.GameObject.Network.Owner != caller )
			return;

		slotMachine.HostSpin( player, caller );
	}

	private void HostSpin( Player player, Connection connection )
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

		if ( player.Money < BetCost )
		{
			SendRejectedToOwner( connection, GameLocalization.Phrase( "ui.shop.not_enough_money", "Not enough money" ) );
			return;
		}

		var session = GetSession( steamId );
		if ( session.IsSpinning )
		{
			SendRejectedToOwner( connection, GameLocalization.Phrase( "ui.casino.spinning_short", "Spinning" ) );
			return;
		}

		player.Money -= BetCost;
		session.IsSpinning = true;
		_sessionsBySteamId[steamId] = session;
		SetCooldown( steamId, MathF.Max( ActionCooldownSeconds, SpinDuration ) );

		var openChance = GetOpenChanceForLock( session.LocksOpened );
		var smallChance = GetSafeSmallPayoutChance( openChance );
		var roll = Game.Random.Float();

		var outcome = "reset";
		var payout = 0;
		var lostValue = BetCost;

		if ( roll <= openChance )
		{
			session.LocksOpened = Math.Clamp( session.LocksOpened + 1, 0, RequiredLocks );
			outcome = session.LocksOpened >= RequiredLocks ? "jackpot" : "open";

			if ( outcome == "jackpot" )
			{
				payout = GetJackpotPayout();
				player.Money += payout;
				_sessionsBySteamId.Remove( steamId );
			}
			else
			{
				session.IsSpinning = false;
				_sessionsBySteamId[steamId] = session;
			}
		}
		else if ( roll <= openChance + smallChance )
		{
			outcome = "small";
			payout = GetSmallPayout();
			player.Money += payout;

			session.IsSpinning = false;
			_sessionsBySteamId[steamId] = session;
		}
		else
		{
			lostValue = BetCost;
			_sessionsBySteamId.Remove( steamId );
		}

		using ( Rpc.FilterInclude( c => c.SteamId.Value == steamId ) )
		{
			RpcOwnerStartSpin(
				outcome,
				outcome == "open" || outcome == "small" ? session.LocksOpened : 0,
				payout,
				lostValue,
				MathF.Max( MinRevealDelay, SpinDuration ) );
		}
	}

	[Rpc.Broadcast]
	private void RpcOwnerStartSpin( string outcome, int locksOpened, int payout, int lostValue, float revealDelay )
	{
		_localVisualState = "spinning";
		_localStatusText = GameLocalization.Phrase( "ui.casino.cracking", "Cracking..." );

		_pendingOutcome = string.IsNullOrWhiteSpace( outcome ) ? "reset" : outcome;
		_pendingLocksOpened = Math.Clamp( locksOpened, 0, RequiredLocks );
		_pendingPayout = Math.Max( 0, payout );
		_pendingLostValue = Math.Max( 0, lostValue );

		_pendingReveal = true;
		_pendingRevealTimer = MathF.Max( MinRevealDelay, revealDelay );
	}

	[Rpc.Broadcast]
	private void RpcOwnerReject( string reason, int locksOpened )
	{
		if ( _pendingReveal )
		{
			_localStatusText = reason;
			return;
		}

		_pendingReveal = false;
		_localLocksOpened = Math.Clamp( locksOpened, 0, RequiredLocks );
		_localStatusText = reason;
		_localVisualState = "reject";
	}

	private void SendRejectedToOwner( Connection connection, string reason )
	{
		var session = GetSession( connection.SteamId.Value );

		using ( Rpc.FilterInclude( c => c.SteamId.Value == connection.SteamId.Value ) )
		{
			RpcOwnerReject( reason, session.LocksOpened );
		}
	}

	private SafeCrackSession GetSession( long steamId )
	{
		if ( _sessionsBySteamId.TryGetValue( steamId, out var session ) )
			return session;

		return default;
	}

	public int GetJackpotPayout()
	{
		return RoundMoney( BetCost * MathF.Max( 0.01f, JackpotMultiplier ) );
	}

	public int GetSmallPayout()
	{
		return RoundMoney( BetCost * MathF.Max( 0.01f, SmallPayoutMultiplier ) );
	}

	public float GetOpenChanceForLock( int locksOpened )
	{
		var safeLocks = Math.Clamp( locksOpened, 0, RequiredLocks - 1 );
		var chance = OpenChance - OpenChanceDropPerLock * safeLocks;
		return Math.Clamp( chance, Math.Clamp( MinOpenChance, 0.01f, 0.95f ), 0.95f );
	}

	private float GetSafeSmallPayoutChance( float openChance )
	{
		var maxSmallChance = MathF.Max( 0f, 0.98f - Math.Clamp( openChance, 0f, 0.98f ) );
		return Math.Clamp( SmallPayoutChance, 0f, maxSmallChance );
	}

	private int RoundMoney( float value )
	{
		var roundTo = Math.Max( 1, PayoutRoundTo );
		var rounded = MathF.Round( value / roundTo ) * roundTo;
		return Math.Max( 1, (int)rounded );
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
		_localLocksOpened = 0;
		_localStatusText = GameLocalization.Phrase( "ui.common.ready", "Ready" );
		_localVisualState = "idle";
	}

	protected override void OnUpdate()
	{
		if ( !_pendingReveal )
			return;

		if ( !_pendingRevealTimer )
			return;

		_pendingReveal = false;

		switch ( _pendingOutcome )
		{
			case "open":
				_localLocksOpened = _pendingLocksOpened;
				_localVisualState = "open";
				_localStatusText = GameLocalization.Format( "ui.casino.lock_progress", "Lock {0}/{1}", _localLocksOpened, RequiredLocks );
				Notification.Info( GameLocalization.Format( "notify.casino.safe_lock", "Safe crack: lock {0}/{1}", _localLocksOpened, RequiredLocks ), 2.1f );
				break;
			case "small":
				_localLocksOpened = _pendingLocksOpened;
				_localVisualState = "small";
				_localStatusText = GameLocalization.Format( "ui.casino.found_money", "Found +${0}", _pendingPayout );
				Notification.Info( GameLocalization.Format( "notify.casino.safe_found", "Safe crack found cash: +${0}", _pendingPayout ), 2.1f );
				break;
			case "jackpot":
				_localLocksOpened = 0;
				_localVisualState = "jackpot";
				_localStatusText = GameLocalization.Format( "ui.casino.jackpot_money", "Jackpot +${0}", _pendingPayout );
				Notification.Info( GameLocalization.Format( "notify.casino.safe_jackpot", "Safe crack jackpot: +${0}", _pendingPayout ), 2.6f );
				break;
			default:
				_localLocksOpened = 0;
				_localVisualState = "reset";
				_localStatusText = GameLocalization.Format( "ui.casino.reset_money", "Reset -${0}", _pendingLostValue );
				Notification.Error( GameLocalization.Format( "notify.casino.safe_reset", "Safe crack reset: -${0}", _pendingLostValue ), 2.2f );
				break;
		}
	}

	private struct SafeCrackSession
	{
		public int LocksOpened;
		public bool IsSpinning;
	}
}
