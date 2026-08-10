using Sandbox;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Minimal.Toolgun;

public enum ToolConfigType
{
	Bool,
	String,
	Number,
	Button,
	Slider
}

public sealed class ToolConfigButton
{
	public string Label { get; init; }
	public string Value { get; init; }
	public string Swatch { get; init; }
}

public sealed class ToolConfigField
{
	public string Id { get; init; }
	public string Label { get; init; }
	public ToolConfigType Type { get; init; }
	public IReadOnlyList<ToolConfigButton> Buttons { get; init; } = Array.Empty<ToolConfigButton>();
	public float Min { get; init; }
	public float Max { get; init; }
	public float Step { get; init; } = 1f;
	public string DefaultValue { get; init; }
}

public sealed class ToolUseContext
{
	public global::Player Player { get; init; }
	public Connection Caller { get; init; }
	public SceneTraceResult Trace { get; init; }
	public global::PropCustom TargetProp { get; init; }
	public IReadOnlyDictionary<string, string> Config { get; init; } = new Dictionary<string, string>();
	public bool IsSecondary { get; init; }
}

public readonly struct ToolUseResult
{
	public bool Success { get; }
	public string Message { get; }

	public ToolUseResult(bool success, string message)
	{
		Success = success;
		Message = message;
	}

	public static ToolUseResult Ok(string message) => new(true, message);
	public static ToolUseResult Fail(string message) => new(false, message);
}

public abstract class ToolMode
{
	public static IReadOnlyList<ToolMode> All => new ToolMode[]
	{
		new RemoverTool(),
		new ColorTool(),
		new FadingDoorTool(),
		new NoCollideTool(),
		new PushTool(),
		new TextscreenTool(),
		new StackerTool()
	};

	public static ToolMode Get(string id)
	{
		var normalized = (id ?? string.Empty).Trim();
		return All.FirstOrDefault(tool => string.Equals(tool.Id, normalized, StringComparison.OrdinalIgnoreCase));
	}

	public static IReadOnlyDictionary<string, string> ParseConfig(string json)
	{
		if (string.IsNullOrWhiteSpace(json))
			return new Dictionary<string, string>();

		try
		{
			return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
		}
		catch (Exception e)
		{
			Log.Warning($"[Toolgun] Failed to parse tool config: {e.Message}");
			return new Dictionary<string, string>();
		}
	}

	public abstract string Id { get; }
	public abstract string Title { get; }
	public abstract string Description { get; }
	public virtual bool RequiresOwnedProp => true;
	public virtual bool SupportsSecondary => false;
	public virtual IReadOnlyList<ToolConfigField> ConfigFields => Array.Empty<ToolConfigField>();

	public abstract ToolUseResult Use(ToolUseContext context);
	public virtual ToolUseResult UseSecondary(ToolUseContext context) => ToolUseResult.Fail(null);

	public virtual ToolUseResult Validate(ToolUseContext context)
	{
		if (!context.Player.IsValid())
			return ToolUseResult.Fail(GameLocalization.Phrase( "notify.player.not_ready", "Your player is not ready." ));

		if (context.Player.IsArrested || !context.Player.IsAlive)
			return ToolUseResult.Fail(GameLocalization.Phrase( "notify.toolgun.cannot_use_now", "You cannot use the Toolgun right now." ));

		if (!RequiresOwnedProp)
			return ToolUseResult.Ok(null);

		if (!context.TargetProp.IsValid() || !context.TargetProp.GameObject.IsValid())
			return ToolUseResult.Fail(GameLocalization.Phrase( "notify.toolgun.aim_own_prop", "Aim the Toolgun at your prop." ));

		if (context.TargetProp.PlayerOwner != context.Player)
			return ToolUseResult.Fail(GameLocalization.Phrase( "notify.toolgun.only_own_props", "You can only work with your own props." ));

		return ToolUseResult.Ok(null);
	}
}

public static class ToolgunClientState
{
	private static readonly Dictionary<string, string> Config = new(StringComparer.Ordinal);
	private static bool _textscreenBulkSizeMigrated;

	public static string SelectedToolId { get; private set; }
	public static int Version { get; private set; }

	public static ToolMode SelectedTool => ToolMode.Get(SelectedToolId);

	public static string SelectedToolTitle => SelectedTool?.Title;

	public static void SelectTool(string id)
	{
		var normalized = (id ?? string.Empty).Trim();
		if (string.Equals(SelectedToolId, normalized, StringComparison.Ordinal))
		{
			EnsureDefaults(SelectedTool);
			return;
		}

		SelectedToolId = normalized;
		EnsureDefaults(SelectedTool);
		Version++;
	}

	public static void EnsureDefaults(ToolMode tool)
	{
		if (tool is null)
			return;

		var changed = false;
		foreach (var field in tool.ConfigFields)
		{
			if (field is null || string.IsNullOrWhiteSpace(field.Id))
				continue;
			if (Config.TryGetValue(field.Id, out var existing))
			{
				if (TryNormalizeFieldValue(field, existing, out var normalized)
					&& !string.Equals(existing, normalized, StringComparison.Ordinal))
				{
					Config[field.Id] = normalized;
					changed = true;
				}

				continue;
			}

			var defaultValue = field.DefaultValue;
			if (defaultValue is null && field.Type == ToolConfigType.Button)
				defaultValue = field.Buttons.FirstOrDefault()?.Value;
			if (defaultValue is null && (field.Type == ToolConfigType.Number || field.Type == ToolConfigType.Slider))
				defaultValue = field.Min.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
			if (defaultValue is null)
				continue;

			if (TryNormalizeFieldValue(field, defaultValue, out var normalizedDefault))
				defaultValue = normalizedDefault;

			Config[field.Id] = defaultValue;
			changed = true;
		}

		if (tool is TextscreenTool)
			MigrateTextscreenConfig();

		if (changed)
			Version++;
	}

	private static void MigrateTextscreenConfig()
	{
		var line0TextKey = TextscreenTool.TextKey(0);
		if (string.IsNullOrWhiteSpace(GetConfigValue(line0TextKey)))
			SetConfigValue(line0TextKey, TextscreenTool.DefaultLineText);

		for (var i = 0; i < TextscreenTool.LineCount; i++)
		{
			var sizeKey = TextscreenTool.SizeKey(i);
			if (!Config.TryGetValue(sizeKey, out var existing))
				continue;

			if (string.Equals(existing, "14", StringComparison.Ordinal))
				SetConfigValue(sizeKey, TextscreenTool.DefaultSize.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
		}

		if (_textscreenBulkSizeMigrated)
			return;

		var allLinesAtMin = true;
		for (var i = 0; i < TextscreenTool.LineCount; i++)
		{
			var raw = GetConfigValue(TextscreenTool.SizeKey(i));
			if (!string.Equals(raw, TextscreenTool.MinSize.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal))
			{
				allLinesAtMin = false;
				break;
			}
		}

		if (!allLinesAtMin)
			return;

		_textscreenBulkSizeMigrated = true;

		for (var i = 0; i < TextscreenTool.LineCount; i++)
		{
			SetConfigValue(
				TextscreenTool.SizeKey(i),
				TextscreenTool.DefaultSize.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
		}
	}

	public static string GetConfigValue(string key)
	{
		return !string.IsNullOrWhiteSpace(key) && Config.TryGetValue(key, out var value) ? value : null;
	}

	public static void SetConfigValue(string key, string value)
	{
		if (string.IsNullOrWhiteSpace(key))
			return;

		var normalized = value ?? string.Empty;
		if (Config.TryGetValue(key, out var current) && string.Equals(current, normalized, StringComparison.Ordinal))
			return;

		Config[key] = normalized;
		Version++;
	}

	public static string GetConfigJson()
	{
		return JsonSerializer.Serialize(Config);
	}

	private static bool TryNormalizeFieldValue(ToolConfigField field, string value, out string normalized)
	{
		normalized = value ?? string.Empty;

		if (field.Type != ToolConfigType.Number && field.Type != ToolConfigType.Slider)
			return false;

		if (!float.TryParse(normalized, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number))
		{
			if (field.DefaultValue is null
				|| !float.TryParse(field.DefaultValue, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out number))
			{
				number = field.Min;
			}
		}

		number = Math.Clamp(number, field.Min, field.Max);

		var step = field.Step <= 0f ? 1f : field.Step;
		if (field.Type == ToolConfigType.Slider)
			number = MathF.Round(number / step) * step;

		number = Math.Clamp(number, field.Min, field.Max);
		normalized = number.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
		return true;
	}
}
