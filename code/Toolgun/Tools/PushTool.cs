using Sandbox;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Minimal.Toolgun;

public sealed class PushTool : ToolMode
{
	public const string UnitsConfigKey = "units";

	private static readonly IReadOnlyList<ToolConfigField> Fields = new[]
	{
		new ToolConfigField
		{
			Id = UnitsConfigKey,
			Label = "Units",
			Type = ToolConfigType.Slider,
			Min = 1f,
			Max = 50f,
			Step = 1f,
			DefaultValue = "1"
		}
	};

	public override string Id => "push";
	public override string Title => "Push";
	public override string Description => "Primary fire pushes your prop away, secondary fire pulls it toward you.";
	public override bool SupportsSecondary => true;
	public override IReadOnlyList<ToolConfigField> ConfigFields => Fields;

	public override ToolUseResult Use(ToolUseContext context)
	{
		return Move(context, awayFromPlayer: true);
	}

	public override ToolUseResult UseSecondary(ToolUseContext context)
	{
		return Move(context, awayFromPlayer: false);
	}

	private static ToolUseResult Move(ToolUseContext context, bool awayFromPlayer)
	{
		var units = GetUnits(context.Config);

		var propObject = context.TargetProp.GameObject;
		var rotation = propObject.WorldRotation;
		var direction = context.Trace.Direction.LengthSquared > 0.001f
			? context.Trace.Direction.Normal
			: Vector3.Forward;

		propObject.WorldPosition += direction * units * (awayFromPlayer ? 1f : -1f);
		propObject.WorldRotation = rotation;
		FreezeProp(propObject);

		return ToolUseResult.Ok(null);
	}

	private static void FreezeProp(GameObject propObject)
	{
		if (!propObject.IsValid())
			return;

		var body = propObject.Components.Get<Rigidbody>(FindMode.EverythingInSelfAndDescendants);
		if (!body.IsValid() || body.IsProxy)
			return;

		body.Velocity = Vector3.Zero;
		body.AngularVelocity = Vector3.Zero;
		body.MotionEnabled = false;
	}

	private static float GetUnits(IReadOnlyDictionary<string, string> config)
	{
		var raw = config.TryGetValue(UnitsConfigKey, out var value) ? value : "1";
		if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var units))
			units = 1f;

		return Math.Clamp(MathF.Round(units), 1f, 50f);
	}
}
