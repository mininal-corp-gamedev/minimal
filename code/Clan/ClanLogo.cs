using System;
using System.Text.Json.Serialization;

namespace Minimal.Clan;

public sealed class ClanLogo
{
	[JsonPropertyName( "iconPath" )] public string IconPath { get; set; } = "";
	[JsonPropertyName( "backgroundType" )] public ClanBackgroundType BackgroundType { get; set; } = ClanBackgroundType.Mono;
	[JsonPropertyName( "backgroundColor1" )] public string BackgroundColor1 { get; set; } = "black";
	[JsonPropertyName( "backgroundColor2" )] public string BackgroundColor2 { get; set; } = "black";
	[JsonPropertyName( "backgroundColor3" )] public string BackgroundColor3 { get; set; } = "black";
	[JsonPropertyName( "backgroundColor4" )] public string BackgroundColor4 { get; set; } = "black";

	public void Normalize()
	{
		IconPath = ClanLogoCatalog.NormalizeIconPath( IconPath );
		BackgroundType = Enum.IsDefined( typeof( ClanBackgroundType ), BackgroundType ) ? BackgroundType : ClanBackgroundType.Mono;
		BackgroundColor1 = ClanPalette.NormalizeColorId( BackgroundColor1, "black" );
		BackgroundColor2 = ClanPalette.NormalizeColorId( BackgroundColor2, "black" );
		BackgroundColor3 = ClanPalette.NormalizeColorId( BackgroundColor3, "black" );
		BackgroundColor4 = ClanPalette.NormalizeColorId( BackgroundColor4, "black" );
	}
}
