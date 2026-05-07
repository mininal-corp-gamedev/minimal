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
	Button
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
}

public sealed class ToolUseContext
{
	public global::Player Player { get; init; }
	public Connection Caller { get; init; }
	public SceneTraceResult Trace { get; init; }
	public global::PropCustom TargetProp { get; init; }
	public IReadOnlyDictionary<string, string> Config { get; init; } = new Dictionary<string, string>();
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
	private static readonly Lazy<IReadOnlyList<ToolMode>> LazyTools = new(() => new ToolMode[]
	{
		new RemoverTool(),
		new ColorTool(),
		new FadingDoorTool()
	});

	public static IReadOnlyList<ToolMode> All => LazyTools.Value;

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
	public virtual IReadOnlyList<ToolConfigField> ConfigFields => Array.Empty<ToolConfigField>();

	public abstract ToolUseResult Use(ToolUseContext context);

	public virtual ToolUseResult Validate(ToolUseContext context)
	{
		if (!context.Player.IsValid())
			return ToolUseResult.Fail("Твой игрок ещё не готов.");

		if (context.Player.IsArrested || !context.Player.IsAlive)
			return ToolUseResult.Fail("Сейчас нельзя использовать Toolgun.");

		if (!RequiresOwnedProp)
			return ToolUseResult.Ok(null);

		if (!context.TargetProp.IsValid() || !context.TargetProp.GameObject.IsValid())
			return ToolUseResult.Fail("Наведи Toolgun на свой prop.");

		if (context.TargetProp.PlayerOwner != context.Player)
			return ToolUseResult.Fail("Можно работать только со своими prop.");

		return ToolUseResult.Ok(null);
	}
}

public static class ToolgunClientState
{
	private static readonly Dictionary<string, string> Config = new(StringComparer.Ordinal);

	public static string SelectedToolId { get; private set; }
	public static int Version { get; private set; }

	public static ToolMode SelectedTool => ToolMode.Get(SelectedToolId);

	public static string SelectedToolTitle => SelectedTool?.Title;

	public static void SelectTool(string id)
	{
		var normalized = (id ?? string.Empty).Trim();
		if (string.Equals(SelectedToolId, normalized, StringComparison.Ordinal))
			return;

		SelectedToolId = normalized;
		Version++;
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
}
