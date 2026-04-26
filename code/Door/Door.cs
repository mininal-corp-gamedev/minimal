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

	/// <summary>Gameplay owner of this door. Set by Buy, cleared by Sell. Not the network object owner.</summary>
	[Sync] public Player PlayerOwner { get; private set; }

	/// <summary>True if the door currently has a gameplay owner.</summary>
	public bool HasOwner => PlayerOwner.IsValid();

	/// <summary>True on the local client if the local Player is the gameplay owner of this door.</summary>
	public bool IsLocalPlayerOwner => HasOwner && PlayerOwner == Player.Local;

	/// <summary>Display name of the gameplay owner, or empty string if none.</summary>
	public string OwnerDisplayName => PlayerOwner.IsValid() ? ( PlayerOwner.Network.Owner?.DisplayName ?? "" ) : "";

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

	private float _currentYaw;
	private Rotation _baseRotation;
	private TimeUntil _brokenLockout;

	/// <summary>True while the post-break cooldown prevents closing or locking.</summary>
	private bool IsBrokenLocked => IsBroken && !_brokenLockout;

	protected override void OnStart()
	{
		_baseRotation = LocalRotation;
		_currentYaw = State == DoorState.Open ? 90f : 0f;
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

	/// <summary>Opens the door. Has no effect if animating, already open, or locked.</summary>
	public void Open()
	{
		if ( IsPlayingAnimation ) return;
		if ( State == DoorState.Open ) return;
		if ( LockState == DoorLockState.Locked ) return;

		State = DoorState.Open;
		IsPlayingAnimation = true;
	}

	[Rpc.Host]
	public void RpcRequestOpen()
	{
		Open();
	}

	/// <summary>Closes the door. Has no effect if animating, already closed, or the broken lockout is active.</summary>
	public void Close()
	{
		if ( IsPlayingAnimation ) return;
		if ( State == DoorState.Closed ) return;
		if ( IsBrokenLocked ) return;

		State = DoorState.Closed;
		IsPlayingAnimation = true;
	}

	[Rpc.Host]
	public void RpcRequestClose()
	{
		Close();
	}

	/// <summary>Locks the door. Requires a non-blocked, non-broken, closed, idle door that is either player-owned or a job-door.</summary>
	public void Lock()
	{
		if ( IsPlayingAnimation ) return;
		if ( State == DoorState.Open ) return;
		if ( IsBrokenLocked ) return;
		if ( IsBlocked ) return;
		// Must be either a job-door or player-owned.
		if ( !HasOnlyJobs && !HasOwner ) return;

		LockState = DoorLockState.Locked;
	}

	[Rpc.Host]
	public void RpcRequestLock()
	{
		if ( !Networking.IsHost ) return;
		if ( !CallerCanLock( Rpc.Caller.SteamId ) ) return;
		Lock();
	}

	/// <summary>Unlocks the door. Requires the door not being admin-blocked and being either player-owned or a job-door.</summary>
	public void Unlock()
	{
		if ( IsBlocked ) return;
		if ( !HasOnlyJobs && !HasOwner ) return;

		LockState = DoorLockState.Unlocked;
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

		return IsOwnedBy( steamId );
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
			PlayerOwner.Money += SellPrice;

		PlayerOwner = null;
		LockState = DoorLockState.Unlocked;
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

		// Owner left the game — release the door.
		PlayerOwner = null;
		LockState = DoorLockState.Unlocked;
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

	[Rpc.Broadcast]
	public void RpcHui(SteamId sid)
	{
        using (Rpc.FilterInclude(connect => connect.SteamId == sid))
		{ 
			//
		}
    }
}
