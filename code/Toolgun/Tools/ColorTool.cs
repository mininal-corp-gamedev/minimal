using Sandbox;
using System;
using System.Collections.Generic;

namespace Minimal.Toolgun;

public sealed class ColorTool : ToolMode
{
	public const string ColorConfigKey = "color";

	private static readonly IReadOnlyDictionary<string, Color> Colors = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
	{
		["white"] = Color.White,
		["black"] = Color.Black,
		["red"] = Color.Red,
		["green"] = Color.Green,
		["blue"] = Color.Blue,
		["yellow"] = new Color(1f, 0.9f, 0f),
		["cyan"] = new Color(0f, 0.85f, 1f),
		["magenta"] = new Color(1f, 0f, 1f),
		["orange"] = new Color(1f, 0.45f, 0f),
		["purple"] = new Color(0.55f, 0.22f, 1f)
	};

	private static readonly IReadOnlyList<ToolConfigField> Fields = new[]
	{
		new ToolConfigField
		{
			Id = ColorConfigKey,
			Label = "Color",
			Type = ToolConfigType.Button,
			Buttons = new[]
			{
				new ToolConfigButton { Label = "White", Value = "white", Swatch = "#ffffff" },
				new ToolConfigButton { Label = "Black", Value = "black", Swatch = "#050505" },
				new ToolConfigButton { Label = "Red", Value = "red", Swatch = "#ef4444" },
				new ToolConfigButton { Label = "Green", Value = "green", Swatch = "#22c55e" },
				new ToolConfigButton { Label = "Blue", Value = "blue", Swatch = "#3b82f6" },
				new ToolConfigButton { Label = "Yellow", Value = "yellow", Swatch = "#facc15" },
				new ToolConfigButton { Label = "Cyan", Value = "cyan", Swatch = "#22d3ee" },
				new ToolConfigButton { Label = "Magenta", Value = "magenta", Swatch = "#d946ef" },
				new ToolConfigButton { Label = "Orange", Value = "orange", Swatch = "#f97316" },
				new ToolConfigButton { Label = "Purple", Value = "purple", Swatch = "#8b5cf6" }
			}
		}
	};

	public override string Id => "color";
	public override string Title => "Color";
	public override string Description => "Красит твой prop выбранным цветом.";
	public override IReadOnlyList<ToolConfigField> ConfigFields => Fields;

	public override ToolUseResult Use(ToolUseContext context)
	{
		var colorId = context.Config.TryGetValue(ColorConfigKey, out var value) ? value : "white";
		if (!Colors.TryGetValue(colorId ?? string.Empty, out var color))
			color = Color.White;

		context.TargetProp.SetTint(color);
		return ToolUseResult.Ok("Prop покрашен.");
	}
}
