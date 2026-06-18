using Sandbox;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Minimal.Toolgun;

public sealed class TextscreenTool : ToolMode
{
	public const string BackgroundConfigKey = "background";
	public const int LineCount = global::Textscreen.LineCount;
	public const int MinSize = global::Textscreen.MinSize;
	public const int MaxSize = global::Textscreen.MaxSize;
	public const int DefaultSize = global::Textscreen.DefaultSize;
	public const string DefaultLineText = global::Textscreen.DefaultLineText;
	public const string DefaultColor = global::Textscreen.DefaultColor;
	private const float FallbackSpawnDistance = 120f;
	private const float SpawnSurfaceOffset = 6f;
	private const string PrefabPath = "prefabs/textscreen.prefab";

	public static readonly IReadOnlyList<ToolConfigButton> ColorButtons = new[]
	{
		new ToolConfigButton { Label = "White", Value = "white", Swatch = "#ffffff" },
		new ToolConfigButton { Label = "Black", Value = "black", Swatch = "#050505" },
		new ToolConfigButton { Label = "Red", Value = "red", Swatch = "#ef4444" },
		new ToolConfigButton { Label = "Green", Value = "green", Swatch = "#22c55e" },
		new ToolConfigButton { Label = "Blue", Value = "blue", Swatch = "#3b82f6" },
		new ToolConfigButton { Label = "Yellow", Value = "yellow", Swatch = "#facc15" },
		new ToolConfigButton { Label = "Cyan", Value = "cyan", Swatch = "#22d3ee" },
		new ToolConfigButton { Label = "Magenta", Value = "magenta", Swatch = "#d946ef" }
	};

	private static readonly IReadOnlyList<ToolConfigField> Fields = BuildFields();

	public override string Id => "textscreen";
	public override string Title => "Textscreen";
	public override string Description => "Spawns a configurable world text screen.";
	public override bool RequiresOwnedProp => false;
	public override IReadOnlyList<ToolConfigField> ConfigFields => Fields;

	public override ToolUseResult Use(ToolUseContext context)
	{
#if SERVER
		var player = context.Player;
		if (player.OwnedPropsCount >= player.MaxProps)
			return ToolUseResult.Fail(GameLocalization.Format("notify.props.limit_reached", "Prop limit reached ({0}).", player.MaxProps));

		var forward = context.Trace.Direction.LengthSquared > 0.001f
			? context.Trace.Direction.Normal
			: player.Controller.EyeTransform.Forward.Normal;

		var position = context.Trace.Hit
			? context.Trace.HitPosition + context.Trace.Normal * SpawnSurfaceOffset
			: player.WorldPosition + forward * FallbackSpawnDistance + Vector3.Up * SpawnSurfaceOffset;

		var facing = -forward;
		if (context.Trace.Hit && context.Trace.Normal.LengthSquared > 0.001f)
			facing = context.Trace.Normal;
		if (facing.LengthSquared <= 0.001f)
			facing = -forward;

		var rotation = Rotation.LookAt(facing.Normal);
		var textscreenObject = GameObject.Clone(PrefabPath, new Transform(position, rotation), null, true, "Textscreen");
		if (!textscreenObject.IsValid())
			return ToolUseResult.Fail(GameLocalization.Phrase("notify.toolgun.textscreen_failed", "Failed to spawn Textscreen."));

		if (!textscreenObject.Components.TryGet<global::Textscreen>(out var textscreen))
			textscreen = textscreenObject.Components.Create<global::Textscreen>();

		var propCustom = global::Textscreen.ConfigurePhysicsShell(textscreenObject, player);
		if (!propCustom.IsValid())
			return ToolUseResult.Fail(GameLocalization.Phrase("notify.toolgun.textscreen_failed", "Failed to spawn Textscreen."));

		player.RegisterSpawnedProp(propCustom);

		textscreen.SetOwner(player);
		textscreen.SetLines(BuildLines(context.Config));
		textscreen.SetBackground(GetBackground(context.Config));

		global::PropCollisionTags.RefreshPhysicsShapeTags(textscreenObject);
		propCustom.TryFreezePhysics();

		textscreenObject.NetworkSpawn();
		OwnedPropNetwork.ConfigurePropCustom(textscreenObject);
		global::PropCollisionTags.RefreshPhysicsShapeTags(textscreenObject);
		return ToolUseResult.Ok(GameLocalization.Phrase("notify.toolgun.textscreen_spawned", "Textscreen spawned."));
#else
		return ToolUseResult.Fail(null);
#endif
	}

	public static string TextKey(int index) => $"line{index}.text";
	public static string SizeKey(int index) => $"line{index}.size";
	public static string ColorKey(int index) => $"line{index}.color";

	public static bool IsTextscreenField(string key)
	{
		return !string.IsNullOrWhiteSpace(key)
			&& (key.Contains(".text", StringComparison.Ordinal)
				|| key.Contains(".size", StringComparison.Ordinal)
				|| key.Contains(".color", StringComparison.Ordinal));
	}

	private static IReadOnlyList<ToolConfigField> BuildFields()
	{
		var fields = new List<ToolConfigField>(LineCount * 3 + 1)
		{
			new ToolConfigField
			{
				Id = BackgroundConfigKey,
				Label = "Background",
				Type = ToolConfigType.Bool,
				DefaultValue = "false"
			}
		};

		for (var i = 0; i < LineCount; i++)
		{
			fields.Add(new ToolConfigField
			{
				Id = TextKey(i),
				Label = $"Line {i + 1}",
				Type = ToolConfigType.String,
				DefaultValue = i == 0 ? DefaultLineText : string.Empty
			});
			fields.Add(new ToolConfigField
			{
				Id = SizeKey(i),
				Label = "Size",
				Type = ToolConfigType.Number,
				Min = MinSize,
				Max = MaxSize,
				Step = 1f,
				DefaultValue = DefaultSize.ToString(CultureInfo.InvariantCulture)
			});
			fields.Add(new ToolConfigField
			{
				Id = ColorKey(i),
				Label = "Color",
				Type = ToolConfigType.Button,
				Buttons = ColorButtons,
				DefaultValue = DefaultColor
			});
		}

		return fields;
	}

	private static bool GetBackground(IReadOnlyDictionary<string, string> config)
	{
		return config.TryGetValue(BackgroundConfigKey, out var value)
			&& string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
	}

	private static IReadOnlyList<global::TextscreenLine> BuildLines(IReadOnlyDictionary<string, string> config)
	{
		var lines = new List<global::TextscreenLine>(LineCount);
		for (var i = 0; i < LineCount; i++)
		{
			var text = config.TryGetValue(TextKey(i), out var textValue) ? textValue ?? string.Empty : string.Empty;
			if (i == 0 && string.IsNullOrWhiteSpace(text))
				text = DefaultLineText;
			var size = DefaultSize;
			if (config.TryGetValue(SizeKey(i), out var rawSize)
				&& int.TryParse(rawSize, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedSize))
			{
				size = Math.Clamp(parsedSize, MinSize, MaxSize);
			}

			var color = config.TryGetValue(ColorKey(i), out var colorValue)
				? global::Textscreen.NormalizeColorId(colorValue)
				: DefaultColor;

			lines.Add(new global::TextscreenLine
			{
				Text = text,
				Size = size,
				Color = color
			});
		}

		return lines;
	}
}
