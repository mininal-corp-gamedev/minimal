using Minimal.Toolgun;
using Sandbox;
using System;

namespace Minimal.Weapons;

public sealed class WeaponToolgun : Weapon
{
	private const float EyePositionTrustDistance = 220f;

	[Property, Category("Combat")] public override float FireDelay { get; set; } = 0.35f;
	[Property, Category("Combat")] public override float AttackRange { get; set; } = 420f;
	[Property, Category("Combat")] public override bool SemiAuto { get; set; } = true;
	[Property, Category("Combat")] public override bool HasHipFire { get; set; } = true;
	[Property, Category("Combat")] public override bool CanAttackWithoutAmmo { get; set; } = true;
	[Property, Category("Reload")] public override bool HasReload { get; set; } = false;

	protected override bool UseDefaultCombatInput => false;

	protected override void OnWeaponStart()
	{
		Ammo = 0;
		Log.Info("[WeaponToolgun] Ready");
	}

	protected override GameObject SpawnBullet(Vector3 origin, Vector3 direction)
	{
		return null;
	}

	protected override void OnWeaponFixedUpdate()
	{
		if (!Player.Local.IsValid() || !Player.Local.Controller.IsValid())
			return;
		if (Player.Local.IsArrested)
			return;
		if (!_timeUntilNextFire)
			return;

		var primaryPressed = Input.Pressed("Attack1");
		var secondaryPressed = !primaryPressed && Input.Pressed("Attack2");
		if (!primaryPressed && !secondaryPressed)
			return;

		var tool = ToolgunClientState.SelectedTool;
		if (tool is null)
			return;
		if (secondaryPressed && !tool.SupportsSecondary)
			return;

		_timeUntilNextFire = FireDelay;
		_firedThisFrame = true;

		var eye = Player.Local.Controller.EyeTransform;
		var configJson = ToolgunClientState.GetConfigJson();

		if (Networking.IsHost)
			HostUseTool(GetLocalPlayerConnection(), tool.Id, configJson, eye.Position, eye.Forward, AttackRange, secondaryPressed);
		else
			RpcRequestUseTool(tool.Id, configJson, eye.Position, eye.Forward, AttackRange, secondaryPressed);

		var origin = ShotPos != null ? ShotPos.WorldPosition : WorldPosition;
		var muzzleRot = ShotPos != null ? ShotPos.WorldRotation : GameObject.WorldRotation;
		Player.Local?.RpcOnWeaponFired(FireSound, origin, null, origin, muzzleRot, null, GetHoldTypeAttack());
	}

	private static Connection GetLocalPlayerConnection()
	{
		return Player.Local?.GameObject.Network.Owner ?? Connection.Local;
	}

	[Rpc.Host]
	private static void RpcRequestUseTool(string toolId, string configJson, Vector3 eyePosition, Vector3 eyeForward, float attackRange, bool isSecondary)
	{
		if (!Networking.IsHost)
			return;

		HostUseTool(Rpc.Caller, toolId, configJson, eyePosition, eyeForward, attackRange, isSecondary);
	}

	private static void HostUseTool(Connection caller, string toolId, string configJson, Vector3 eyePosition, Vector3 eyeForward, float attackRange, bool isSecondary)
	{
		if (!Networking.IsHost)
			return;
		if (caller is null)
			return;

		var player = Player.FindPlayerBySteamId(caller.SteamId.Value);
		if (!player.IsValid() || !player.Controller.IsValid())
		{
			NotifyCaller(caller, GameLocalization.Phrase("notify.player.not_ready", "Your player is not ready."), false);
			return;
		}

		if (!string.Equals(player.EquippedWeaponItemId, "toolgun", StringComparison.OrdinalIgnoreCase))
		{
			NotifyCaller(caller, GameLocalization.Phrase("notify.toolgun.equip_first", "Equip the Toolgun first."), false);
			return;
		}

		var tool = ToolMode.Get(toolId);
		if (tool is null)
		{
			NotifyCaller(caller, GameLocalization.Phrase("notify.toolgun.no_tool_selected", "No tool selected."), false);
			return;
		}
		if (isSecondary && !tool.SupportsSecondary)
			return;

		var eye = player.Controller.EyeTransform;
		var origin = Vector3.DistanceBetween(player.WorldPosition, eyePosition) <= EyePositionTrustDistance
			? eyePosition
			: eye.Position;
		var forward = eyeForward.LengthSquared > 0.001f ? eyeForward.Normal : eye.Forward;
		var range = Math.Clamp(attackRange, 1f, 1000f);

		var trace = player.Scene.Trace
			.Ray(origin, origin + forward * range)
			.IgnoreGameObjectHierarchy(player.GameObject)
			.WithoutTags("player", "bullet")
			.Run();

		var targetProp = trace.Hit && trace.GameObject.IsValid()
			? trace.GameObject.Components.Get<global::PropCustom>(FindMode.EverythingInSelfAndAncestors)
			: null;

		var context = new ToolUseContext
		{
			Player = player,
			Caller = caller,
			Trace = trace,
			TargetProp = targetProp,
			Config = ToolMode.ParseConfig(configJson),
			IsSecondary = isSecondary
		};

		var validation = tool.Validate(context);
		if (!validation.Success)
		{
			NotifyCaller(caller, validation.Message, false);
			return;
		}

		var result = isSecondary ? tool.UseSecondary(context) : tool.Use(context);
		if (!string.IsNullOrWhiteSpace(result.Message))
			NotifyCaller(caller, result.Message, result.Success);
	}

	private static void NotifyCaller(Connection caller, string message, bool success)
	{
		if (caller is null)
			return;

		using (Rpc.FilterInclude(connection => connection.SteamId.Value == caller.SteamId.Value))
		{
			RpcReceiveToolResult(message, success);
		}
	}

	[Rpc.Broadcast]
	private static void RpcReceiveToolResult(string message, bool success)
	{
		if (success)
			Notification.Info(message, 3.5f);
		else
			Notification.Error(message, 3.5f);
	}
}
