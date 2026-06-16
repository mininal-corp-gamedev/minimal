using System;
using System.Collections.Generic;

namespace Minimal.Clan;

public static class ClanPalette
{
	public const string DefaultColorId = "white";

	public static readonly IReadOnlyList<ClanColorOption> Colors = new[]
	{
		new ClanColorOption( "White", "white", "#ffffff" ),
		new ClanColorOption( "Black", "black", "#050505" ),
		new ClanColorOption( "Red", "red", "#ef4444" ),
		new ClanColorOption( "Green", "green", "#22c55e" ),
		new ClanColorOption( "Blue", "blue", "#3b82f6" ),
		new ClanColorOption( "Yellow", "yellow", "#facc15" ),
		new ClanColorOption( "Cyan", "cyan", "#22d3ee" ),
		new ClanColorOption( "Magenta", "magenta", "#d946ef" )
	};

	public static string NormalizeColorId( string colorId, string fallback = DefaultColorId )
	{
		var normalized = (colorId ?? string.Empty).Trim().ToLowerInvariant();
		foreach ( var option in Colors )
		{
			if ( string.Equals( option.Value, normalized, StringComparison.OrdinalIgnoreCase ) )
				return option.Value;
		}

		return string.IsNullOrWhiteSpace( fallback ) ? DefaultColorId : fallback;
	}

	public static string GetSwatch( string colorId )
	{
		var normalized = NormalizeColorId( colorId );
		foreach ( var option in Colors )
		{
			if ( string.Equals( option.Value, normalized, StringComparison.OrdinalIgnoreCase ) )
				return option.Swatch;
		}

		return "#ffffff";
	}
}

public sealed class ClanColorOption
{
	public string Label { get; }
	public string Value { get; }
	public string Swatch { get; }

	public ClanColorOption( string label, string value, string swatch )
	{
		Label = label;
		Value = value;
		Swatch = swatch;
	}
}
