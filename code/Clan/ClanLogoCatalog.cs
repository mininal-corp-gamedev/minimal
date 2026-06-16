using System;
using System.Collections.Generic;

namespace Minimal.Clan;

public static class ClanLogoCatalog
{
	public static readonly IReadOnlyList<ClanLogoOption> Icons = new[]
	{
		new ClanLogoOption( "None", "" ),
		new ClanLogoOption( "AK47", "materials/icons/clan/ak47.vtex" ),
		new ClanLogoOption( "Book", "materials/icons/clan/book.vtex" ),
		new ClanLogoOption( "Bull", "materials/icons/clan/bull.vtex" ),
		new ClanLogoOption( "Bulldog", "materials/icons/clan/bulldog.vtex" ),
		new ClanLogoOption( "Cannabis", "materials/icons/clan/cannabis.vtex" ),
		new ClanLogoOption( "Chain", "materials/icons/clan/chain.vtex" ),
		new ClanLogoOption( "Diamond", "materials/icons/clan/diamond.vtex" ),
		new ClanLogoOption( "Eye", "materials/icons/clan/eye_ctulhu.vtex" ),
		new ClanLogoOption( "Finger", "materials/icons/clan/finger.vtex" ),
		new ClanLogoOption( "Flag", "materials/icons/clan/flag.vtex" ),
		new ClanLogoOption( "Hat", "materials/icons/clan/hat.vtex" ),
		new ClanLogoOption( "Lizard", "materials/icons/clan/lizard.vtex" ),
		new ClanLogoOption( "Moon", "materials/icons/clan/moon.vtex" ),
		new ClanLogoOption( "Pickaxes", "materials/icons/clan/pickaxes.vtex" ),
		new ClanLogoOption( "Swag", "materials/icons/clan/swag.vtex" ),
		new ClanLogoOption( "Triangle", "materials/icons/clan/triangle.vtex" )
	};

	public static string NormalizeIconPath( string iconPath )
	{
		var normalized = (iconPath ?? string.Empty).Trim();
		foreach ( var option in Icons )
		{
			if ( string.Equals( option.Path, normalized, StringComparison.OrdinalIgnoreCase ) )
				return option.Path;
		}

		return "";
	}
}

public sealed class ClanLogoOption
{
	public string Label { get; }
	public string Path { get; }

	public ClanLogoOption( string label, string path )
	{
		Label = label;
		Path = path;
	}
}
