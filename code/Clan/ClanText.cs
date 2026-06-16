namespace Minimal.Clan;

public static class ClanText
{
	public const int HeaderMaxLength = 28;
	public const int DescriptionMaxLength = 160;

	public static string NormalizeHeader( string value )
	{
		var text = (value ?? string.Empty).Trim();
		if ( text.Length > HeaderMaxLength )
			text = text[..HeaderMaxLength];

		return text;
	}

	public static string NormalizeDescription( string value )
	{
		var text = (value ?? string.Empty).Trim();
		if ( text.Length > DescriptionMaxLength )
			text = text[..DescriptionMaxLength];

		return text;
	}
}
