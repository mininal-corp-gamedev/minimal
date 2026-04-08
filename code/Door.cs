using Sandbox;
using System;

/// <summary>A door component with open/close animation, lock state, and break mechanics.</summary>
public sealed class Door : Component, Component.IPressable
{
	public enum DoorState { Closed, Open }
	public enum DoorLockState { Unlocked, Locked }

	/// <summary>Gameplay owner of this door. Not the network object owner — used purely for game design logic.</summary>
	[Property] public Player PlayerOwner { get; set; }

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
	private TimeUntil _brokenLockout;

	/// <summary>True while the post-break cooldown prevents closing or locking.</summary>
	private bool IsBrokenLocked => IsBroken && !_brokenLockout;

	protected override void OnStart()
	{
		_currentYaw = State == DoorState.Open ? 90f : 0f;
		LocalRotation = Rotation.FromYaw( _currentYaw );
	}

	protected override void OnUpdate()
	{
		float targetYaw = State == DoorState.Open ? 90f : 0f;

		if ( !IsPlayingAnimation )
		{
			if ( MathF.Abs( _currentYaw - targetYaw ) > 0.001f )
			{
				_currentYaw = targetYaw;
				LocalRotation = Rotation.FromYaw( _currentYaw );
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

		LocalRotation = Rotation.FromYaw( _currentYaw );
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

	/// <summary>Locks the door. Has no effect if animating, the door is open, or the broken lockout is active.</summary>
	public void Lock()
	{
		if ( IsPlayingAnimation ) return;
		if ( State == DoorState.Open ) return;
		if ( IsBrokenLocked ) return;

		LockState = DoorLockState.Locked;
	}

	[Rpc.Host]
	public void RpcRequestLock()
	{
		Lock();
	}

	/// <summary>Unlocks the door.</summary>
	public void Unlock()
	{
		LockState = DoorLockState.Unlocked;
	}

	[Rpc.Host]
	public void RpcRequestUnlock()
	{
		Unlock();
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
