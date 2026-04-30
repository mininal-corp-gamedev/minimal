using Sandbox;
using System;
using System.Collections.Generic;

/// <summary>A door component with open/close animation, lock state, and break mechanics.</summary>
public sealed class Door : Component, Component.IPressable, Component.INetworkListener
{
	public enum DoorState { Closed, Open }
	public enum DoorLockState { Unlocked, Locked }

	/// <summary>Display name shown on the world HUD when the door has no owner or is blocked.</summary>
	[Property] public string Header { get; set; } = "Door";

	[Property] public DoorWorldHud WorldHud { get; set; }

	/// <summary>If true, the door is admin-blocked: it cannot be bought, sold, locked, or unlocked. Only the header is shown on the HUD.</summary>
	[Property] public bool IsBlocked { get; set; } = false;

	/// <summary>Buy price of the door. Selling refunds half of this value.</summary>
	[Property] public int BuyPrice { get; set; } = 50;

	/// <summary>Sell price — half of <see cref="BuyPrice"/>.</summary>
	public int SellPrice => BuyPrice / 2;

	/// <summary>If true, the door belongs to specific jobs: it cannot be bought, and only players holding an allowed job can lock/unlock it.</summary>
	[Property] public bool HasOnlyJobs { get; set; } = false;

	/// <summary>Jobs allowed to lock/unlock this door when <see cref="HasOnlyJobs"/> is true.</summary>
	[Property, ShowIf( "HasOnlyJobs", true )] public List<JobDefinition> AllowedJobs { get; set; } = new();

	/// <summary>
	/// Optional paired door. When set, Buy/Sell/Open/Close/Lock/Unlock mirror to the paired door.
	/// Pairing is bidirectional and must be authored manually: set <c>DoorSecond</c> on both doors pointing at each other.
	/// </summary>
	[Property] public Door DoorSecond { get; set; }

	/// <summary>Gameplay owner of this door. Set by Buy, cleared by Sell. Not the network object owner.</summary>
	[Sync] public Player PlayerOwner { get; private set; }

	/// <summary>True if the door currently has a gameplay owner.</summary>
	public bool HasOwner => PlayerOwner.IsValid();

	/// <summary>True on the local client if the local Player is the gameplay owner of this door.</summary>
	public bool IsLocalPlayerOwner => HasOwner && PlayerOwner == Player.Local;

	/// <summary>Display name of the gameplay owner, or empty string if none.</summary>
	public string OwnerDisplayName => PlayerOwner.IsValid() ? ( PlayerOwner.Network.Owner?.DisplayName ?? "" ) : "";

	/// <summary>SteamId of the gameplay owner, or 0 if none.</summary>
	public long OwnerSteamId => PlayerOwner.IsValid() && PlayerOwner.Network.Owner is not null ? PlayerOwner.Network.Owner.SteamId.Value : 0L;

	// ===== Roommates =====

	/// <summary>Host-authoritative, network-synced, semicolon-separated list of roommate SteamIds.</summary>
	[Sync] public string RoommateIdsSerialized { get; private set; } = "";

	/// <summary>Parsed enumeration of roommate SteamIds.</summary>
	public IEnumerable<long> RoommateIds
	{
		get
		{
			if ( string.IsNullOrWhiteSpace( RoommateIdsSerialized ) ) yield break;
			var parts = RoommateIdsSerialized.Split( ';', StringSplitOptions.RemoveEmptyEntries );
			foreach ( var part in parts )
			{
				if ( long.TryParse( part, out var id ) && id != 0L )
					yield return id;
			}
		}
	}

	/// <summary>True if the door type supports roommates (not blocked and not job-only).</summary>
	public bool CanHaveRoommates => !IsBlocked && !HasOnlyJobs;

	public bool IsRoommate( long steamId )
	{
		if ( steamId == 0L ) return false;
		foreach ( var id in RoommateIds )
			if ( id == steamId ) return true;
		return false;
	}

	/// <summary>True on the local client if the local player is listed as a roommate of this door.</summary>
	public bool IsLocalPlayerRoommate
	{
		get
		{
			var conn = Connection.Local;
			if ( conn is null ) return false;
			return IsRoommate( conn.SteamId.Value );
		}
	}

	private bool IsRoommate( ulong steamId ) => IsRoommate( unchecked( (long)steamId ) );

	/// <summary>Returns true if the given player currently holds one of the <see cref="AllowedJobs"/> (only meaningful when <see cref="HasOnlyJobs"/> is true).</summary>
	public bool IsJobAllowed( Player player )
	{
		if ( !HasOnlyJobs ) return false;
		if ( !player.IsValid() ) return false;
		if ( AllowedJobs is null || AllowedJobs.Count == 0 ) return false;

		var def = player.Job?.JobDefinition;
		if ( def is null ) return false;

		return AllowedJobs.Contains( def );
	}

	/// <summary>True on the local client if the local player holds one of the allowed jobs for this door.</summary>
	public bool IsLocalPlayerJobAllowed => IsJobAllowed( Player.Local );

	/// <summary>Current open/closed state of the door.</summary>
	[Sync] public DoorState State { get; private set; } = DoorState.Closed;

	/// <summary>Current lock state of the door.</summary>
	[Sync] public DoorLockState LockState { get; private set; } = DoorLockState.Unlocked;

	/// <summary>True while the door is playing its open or close animation.</summary>
	[Sync] public bool IsPlayingAnimation { get; private set; }

	/// <summary>True if the door has been broken.</summary>
	[Sync] public bool IsBroken { get; private set; }

	/// <summary>Speed of the open/close rotation animation in degrees per second.</summary>
	[Property] public float AnimationSpeed { get; set; } = 90f;

	/// <summary>Sound played (broadcast to all clients at the door position) when this door opens.</summary>
	[Property, Category( "Sounds" )] public SoundEvent OpenSound { get; set; }

	/// <summary>Sound played (broadcast to all clients at the door position) when this door closes.</summary>
	[Property, Category( "Sounds" )] public SoundEvent CloseSound { get; set; }

	/// <summary>
	/// True on the local client if the local player may lock/unlock this door with the Keys weapon:
	/// job-allowed for job doors, or owner/roommate for player-owned doors.
	/// </summary>
	public bool CanBeControlledByLocalPlayer
	{
		get
		{
			if ( IsBlocked ) return false;
			if ( HasOnlyJobs ) return IsLocalPlayerJobAllowed;
			if ( !HasOwner ) return false;
			return IsLocalPlayerOwner || IsLocalPlayerRoommate;
		}
	}

	// ===== Lockpick =====

	/// <summary>How long (seconds) the player cooldown lasts after a lockpick attempt.</summary>
	[Property, Category( "Lockpick" )] public float LockpickCooldownSeconds { get; set; } = 60f;

	/// <summary>Chance (0..1) for a lockpick attempt to succeed.</summary>
	[Property, Category( "Lockpick" ), Range( 0f, 1f )] public float LockpickSuccessChance { get; set; } = 0.5f;

	/// <summary>Maximum distance between the picker and the door for the host to accept an attempt.</summary>
	[Property, Category( "Lockpick" )] public float LockpickInteractRange { get; set; } = 120f;

	private float _currentYaw;
	private Rotation _baseRotation;
	private TimeUntil _brokenLockout;

	/// <summary>True while the post-break cooldown prevents closing or locking.</summary>
	private bool IsBrokenLocked => IsBroken && !_brokenLockout;

	protected override void OnStart()
	{
		_baseRotation = LocalRotation;
		_currentYaw = State == DoorState.Open ? 90f : 0f;

		if ( WorldHud.IsValid() )
			WorldHud.Door = this;
	}

	protected override void OnUpdate()
	{
		float targetYaw = State == DoorState.Open ? 90f : 0f;

		if ( !IsPlayingAnimation )
		{
			if ( MathF.Abs( _currentYaw - targetYaw ) > 0.001f )
			{
				_currentYaw = targetYaw;
				LocalRotation = _baseRotation * Rotation.FromYaw( _currentYaw );
			}
			return;
		}

		float step = AnimationSpeed * Time.Delta;
		float diff = targetYaw - _currentYaw;

		if ( MathF.Abs( diff ) <= step )
		{
			_currentYaw = targetYaw;
			if ( !IsProxy )
				IsPlayingAnimation = false;
		}
		else
		{
			_currentYaw += MathF.Sign( diff ) * step;
		}

		LocalRotation = _baseRotation * Rotation.FromYaw( _currentYaw );
	}

	public bool Press( IPressable.Event e )
	{
		if ( !e.Source.GameObject.Components.TryGet<Player>( out var player, FindMode.EverythingInSelfAndParent ) ) return false;

		if ( State == DoorState.Closed )
			RpcRequestOpen();
		else
			RpcRequestClose();

		return true;
	}

	/// <summary>Opens the door. Has no effect if animating, already open, or locked. Mirrors to <see cref="DoorSecond"/>.</summary>
	public void Open()
	{
		if ( State == DoorState.Open ) return; // idempotent — prevents paired-door recursion
		if ( IsPlayingAnimation ) return;
		if ( LockState == DoorLockState.Locked ) return;

		State = DoorState.Open;
		IsPlayingAnimation = true;

		if ( OpenSound.IsValid() )
			RpcPlaySoundAtDoor( OpenSound );

		DoorSecond?.Open();
	}

	[Rpc.Host]
	public void RpcRequestOpen()
	{
		Open();
	}

	/// <summary>Closes the door. Has no effect if animating, already closed, or the broken lockout is active. Mirrors to <see cref="DoorSecond"/>.</summary>
	public void Close()
	{
		if ( State == DoorState.Closed ) return; // idempotent — prevents paired-door recursion
		if ( IsPlayingAnimation ) return;
		if ( IsBrokenLocked ) return;

		State = DoorState.Closed;
		IsPlayingAnimation = true;

		if ( CloseSound.IsValid() )
			RpcPlaySoundAtDoor( CloseSound );

		DoorSecond?.Close();
	}

	[Rpc.Host]
	public void RpcRequestClose()
	{
		Close();
	}

	/// <summary>Locks the door. Requires a non-blocked, non-broken, closed, idle door that is either player-owned or a job-door. Mirrors to <see cref="DoorSecond"/>.</summary>
	public void Lock()
	{
		if ( LockState == DoorLockState.Locked ) return; // idempotent — prevents paired-door recursion
		if ( IsPlayingAnimation ) return;
		if ( State == DoorState.Open ) return;
		if ( IsBrokenLocked ) return;
		if ( IsBlocked ) return;
		// Must be either a job-door or player-owned.
		if ( !HasOnlyJobs && !HasOwner ) return;

		LockState = DoorLockState.Locked;
		WorldHud?.WorldHudRefresh();

		DoorSecond?.Lock();
	}

	[Rpc.Host]
	public void RpcRequestLock()
	{
		if ( !Networking.IsHost ) return;
		if ( !CallerCanLock( Rpc.Caller.SteamId ) ) return;
		Lock();
	}

	/// <summary>Unlocks the door. Requires the door not being admin-blocked and being either player-owned or a job-door. Mirrors to <see cref="DoorSecond"/>.</summary>
	public void Unlock()
	{
		if ( LockState == DoorLockState.Unlocked ) return; // idempotent — prevents paired-door recursion
		if ( IsBlocked ) return;
		if ( !HasOnlyJobs && !HasOwner ) return;

		LockState = DoorLockState.Unlocked;
		WorldHud?.WorldHudRefresh();

		DoorSecond?.Unlock();
	}

	[Rpc.Host]
	public void RpcRequestUnlock()
	{
		if ( !Networking.IsHost ) return;
		if ( !CallerCanLock( Rpc.Caller.SteamId ) ) return;
		Unlock();
	}

	/// <summary>Host-side check: is this caller allowed to toggle the door's lock state?</summary>
	private bool CallerCanLock( ulong steamId )
	{
		if ( HasOnlyJobs )
		{
			var caller = FindPlayerBySteamId( steamId );
			return IsJobAllowed( caller );
		}

		if ( IsOwnedBy( steamId ) ) return true;
		if ( IsRoommate( steamId ) ) return true;
		return false;
	}

	/// <summary>Buys the door for the given player. Host-authoritative: validates blocked state, ownership, and funds.</summary>
	public void Buy( Player buyer )
	{
		if ( !Networking.IsHost ) return;
		if ( !buyer.IsValid() ) return;
		if ( IsBlocked ) return;
		if ( HasOnlyJobs ) return; // Job-only doors cannot be bought.
		if ( HasOwner ) return;
		if ( buyer.Money < BuyPrice ) return;
		if ( buyer.OwnedDoorsCount >= buyer.MaxDoors ) return; // Player has reached their door limit.

		buyer.Money -= BuyPrice;
		PlayerOwner = buyer;
		WorldHud?.WorldHudRefresh();

		// Mirror ownership to the paired door without charging again.
		if ( DoorSecond.IsValid() && !DoorSecond.HasOwner )
		{
			DoorSecond.PlayerOwner = buyer;
		}
	}

	[Rpc.Host]
	public void RpcRequestBuy()
	{
		if ( !Networking.IsHost ) return;

		var buyer = FindPlayerBySteamId( Rpc.Caller.SteamId );
		if ( buyer is null )
		{
			Log.Warning( $"Door.RpcRequestBuy: player not found for {Rpc.Caller.DisplayName}" );
			return;
		}

		Buy( buyer );
	}

	/// <summary>Sells the door. Refunds half the buy price to the owner, clears ownership, and unlocks.</summary>
	public void Sell()
	{
		if ( !Networking.IsHost ) return;
		if ( IsBlocked ) return;
		if ( HasOnlyJobs ) return; // Job-only doors cannot be sold.
		if ( !HasOwner ) return;

		if ( PlayerOwner.IsValid() )
			PlayerOwner.Money += SellPrice; // Refund half, once — not doubled for paired doors.

		var partner = DoorSecond;

		PlayerOwner = null;
		LockState = DoorLockState.Unlocked;
		RoommateIdsSerialized = "";
		WorldHud?.WorldHudRefresh();

		// Clear ownership on the paired door without refunding again.
		if ( partner.IsValid() && partner.HasOwner )
		{
			partner.PlayerOwner = null;
			partner.LockState = DoorLockState.Unlocked;
			partner.RoommateIdsSerialized = "";
		}
	}

	[Rpc.Host]
	public void RpcRequestSell()
	{
		if ( !Networking.IsHost ) return;
		// Only the gameplay owner can sell.
		if ( !IsOwnedBy( Rpc.Caller.SteamId ) ) return;
		Sell();
	}

	/// <summary>Returns true if the given SteamId matches the gameplay owner of this door.</summary>
	public bool IsOwnedBy( ulong steamId )
	{
		if ( !HasOwner ) return false;
		var ownerConn = PlayerOwner.Network.Owner;
		return ownerConn != null && ownerConn.SteamId == steamId;
	}

	/// <summary>
	/// Network event: when any player disconnects, free this door if they were the gameplay owner.
	/// Runs on every connected client; mutation is host-only.
	/// </summary>
	void Component.INetworkListener.OnDisconnected( Connection channel )
	{
		if ( !Networking.IsHost ) return;
		if ( !HasOwner ) return;

		var ownerConn = PlayerOwner.Network.Owner;
		if ( ownerConn is null || ownerConn.SteamId != channel.SteamId ) return;

		// Owner left the game — release the door and clear roommates.
		PlayerOwner = null;
		LockState = DoorLockState.Unlocked;
		RoommateIdsSerialized = "";

		if ( DoorSecond.IsValid() )
		{
			DoorSecond.PlayerOwner = null;
			DoorSecond.LockState = DoorLockState.Unlocked;
			DoorSecond.RoommateIdsSerialized = "";
		}
	}

	/// <summary>Host-authoritative: add a roommate by SteamId. Only the current owner may call.</summary>
	[Rpc.Host]
	public void RpcRequestAddRoommate( long steamId )
	{
		if ( !Networking.IsHost ) return;
		if ( !CanHaveRoommates ) return;
		if ( !HasOwner ) return;
		if ( !IsOwnedBy( Rpc.Caller.SteamId ) ) return; // only the owner can add
		if ( steamId == 0L ) return;
		if ( steamId == OwnerSteamId ) return; // cannot add self

		var set = new HashSet<long>( RoommateIds );
		if ( !set.Add( steamId ) ) return; // already a roommate

		var serialized = string.Join( ";", set );
		RoommateIdsSerialized = serialized;

		if ( DoorSecond.IsValid() )
			DoorSecond.RoommateIdsSerialized = serialized;
	}

	/// <summary>Host-authoritative: remove a roommate by SteamId. Only the current owner may call.</summary>
	[Rpc.Host]
	public void RpcRequestRemoveRoommate( long steamId )
	{
		if ( !Networking.IsHost ) return;
		if ( !CanHaveRoommates ) return;
		if ( !HasOwner ) return;
		if ( !IsOwnedBy( Rpc.Caller.SteamId ) ) return; // only the owner can remove
		if ( steamId == 0L ) return;

		var set = new HashSet<long>( RoommateIds );
		if ( !set.Remove( steamId ) ) return;

		var serialized = string.Join( ";", set );
		RoommateIdsSerialized = serialized;

		if ( DoorSecond.IsValid() )
			DoorSecond.RoommateIdsSerialized = serialized;
	}

	/// <summary>Broadcast-play a sound at this door's world position on all clients.</summary>
	[Rpc.Broadcast]
	public void RpcPlaySoundAtDoor( SoundEvent sound )
	{
		if ( !sound.IsValid() ) return;
		Sound.Play( sound, WorldPosition );
	}

	/// <summary>
	/// Client → Host → Broadcast: play the "hit" sound at the door for everyone.
	/// Used when a player who is neither the owner nor a roommate tries to interact
	/// with the Keys weapon — they only hit the door without changing state.
	/// </summary>
	[Rpc.Host]
	public void RpcRequestHit( SoundEvent hitSound )
	{
		if ( !Networking.IsHost ) return;
		if ( !hitSound.IsValid() ) return;
		RpcPlaySoundAtDoor( hitSound );
	}

	private Player FindPlayerBySteamId( ulong steamId )
	{
		foreach ( var go in Scene.GetAllObjects( true ) )
		{
			if ( !go.Components.TryGet<Player>( out var ply ) ) continue;
			if ( ply.GameObject.Network.Owner?.SteamId == steamId ) return ply;
		}
		return null;
	}

	/// <summary>
	/// Breaks the door: unlocks it, forces it open, and prevents closing or locking
	/// for a duration defined by the broken lockout timer.
	/// </summary>
	public void Break()
	{
		if ( IsBroken ) return;

		IsBroken = true;
		LockState = DoorLockState.Unlocked;
		_brokenLockout = 10f;

		if ( State == DoorState.Closed && !IsPlayingAnimation )
			Open();
	}

	[Rpc.Host]
	public void RpcRequestBreak()
	{
		Break();
	}

	// ===================== LOCKPICK =====================

	/// <summary>True if this door is currently a valid candidate for a fresh lockpick attempt.</summary>
	public bool CanBeLockpicked()
	{
		if ( IsBlocked ) return false;
		if ( IsBroken ) return false;
		if ( LockState != DoorLockState.Locked ) return false;
		// Только покупные (с владельцем) или job-двери — на остальных замок не имеет смысла.
		if ( !HasOnlyJobs && !HasOwner ) return false;
		return true;
	}

	/// <summary>Client request: try to start a lockpick attempt on this door. Host-authoritative.</summary>
	[Rpc.Host]
	public void RpcRequestLockpick()
	{
		if ( !Networking.IsHost ) return;

		var caller = Rpc.Caller;
		if ( caller is null ) return;

		var picker = FindPlayerBySteamId( caller.SteamId );
		if ( !picker.IsValid() ) return;
		if ( picker.IsArrested ) return;

		if ( !CanBeLockpicked() )
		{
			NotifyLockpicker( caller, "Эту дверь нельзя взломать.", NotificationType.Warn, 3.0f );
			return;
		}

		if ( picker.LockpickCooldown > 0f )
		{
			var secondsLeft = (float)picker.LockpickCooldown;
			NotifyLockpicker( caller, $"Подожди {secondsLeft:0}с перед следующей попыткой.", NotificationType.Warn, 2.5f );
			return;
		}

		if ( Vector3.DistanceBetween( picker.WorldPosition, WorldPosition ) > LockpickInteractRange )
		{
			NotifyLockpicker( caller, "Слишком далеко от двери.", NotificationType.Warn, 2.5f );
			return;
		}

		var cooldown = MathF.Max( 0.5f, LockpickCooldownSeconds );

		// Бросок 50/50 происходит сразу, результат сразу отправляется клиенту.
		var roll = Game.Random.Float( 0f, 1f );
		var success = roll < MathF.Max( 0f, MathF.Min( 1f, LockpickSuccessChance ) );

		// Устанавливаем кулдаун на игроке
		picker.LockpickCooldown = cooldown;

		if ( success )
		{
			// Сразу разблокируем и открываем дверь
			LockState = DoorLockState.Unlocked;
			if ( DoorSecond.IsValid() && DoorSecond.LockState == DoorLockState.Locked )
				DoorSecond.LockState = DoorLockState.Unlocked;
			Open();

			NotifyLockpicker( caller, $"Взлом удался! Дверь открыта. Следующая попытка через {cooldown:0}с.", NotificationType.Info, 3.5f );
		}
		else
		{
			NotifyLockpicker( caller, $"Взлом не удался. Следующая попытка через {cooldown:0}с.", NotificationType.Error, 3.5f );
		}
	}

	private static void NotifyLockpicker( Connection target, string text, NotificationType type, float aliveSeconds )
	{
		if ( target is null ) return;

		using ( Rpc.FilterInclude( c => c.SteamId.Value == target.SteamId.Value ) )
		{
			RpcShowLockpickNotification( text, (int)type, aliveSeconds );
		}
	}

	[Rpc.Broadcast]
	private static void RpcShowLockpickNotification( string text, int type, float aliveSeconds )
	{
		var t = (NotificationType)type;
		switch ( t )
		{
			case NotificationType.Warn: Notification.Warn( text, aliveSeconds ); break;
			case NotificationType.Error: Notification.Error( text, aliveSeconds ); break;
			default: Notification.Info( text, aliveSeconds ); break;
		}
	}

	[Rpc.Broadcast]
	public void RpcHui(SteamId sid)
	{
        using (Rpc.FilterInclude(connect => connect.SteamId == sid))
		{ 
			//
		}
    }
}
