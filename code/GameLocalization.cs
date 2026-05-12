using System;
using System.Collections.Generic;
using System.Globalization;
using Ambi.Storage;
using Minimal.Shop;
using Sandbox;

public static partial class GameLocalization
{
	public const string EnglishCode = "en";
	public const string RussianCode = "ru";
	private static string _selectedCode = EnglishCode;
	private static readonly Dictionary<string, Dictionary<string, string>> PhrasesByLanguage = new( StringComparer.OrdinalIgnoreCase );
	public static int Version { get; private set; }

	public static string CurrentCode => string.IsNullOrWhiteSpace( _selectedCode ) ? EnglishCode : _selectedCode;

	public static void SetLanguage( string code )
	{
		var normalized = NormalizeLanguageCode( code );
		if ( string.Equals( _selectedCode, normalized, StringComparison.OrdinalIgnoreCase ) )
			return;

		_selectedCode = normalized;
		Version++;
	}

	public static string ToggleCode => IsRussian ? EnglishCode : RussianCode;
	public static bool IsRussian => string.Equals( CurrentCode, RussianCode, StringComparison.OrdinalIgnoreCase );

	public static string Phrase( string key, string fallback = null )
	{
		var normalizedKey = (key ?? string.Empty).Trim();
		if ( string.IsNullOrWhiteSpace( normalizedKey ) )
			return fallback ?? string.Empty;

		var text = GetCustomPhrase( normalizedKey );
		if ( !string.IsNullOrWhiteSpace( text )
			&& !string.Equals( text, normalizedKey, StringComparison.Ordinal )
			&& !string.Equals( text, $"#{normalizedKey}", StringComparison.Ordinal ) )
			return text;

		return fallback ?? normalizedKey;
	}

	private static string GetCustomPhrase( string key )
	{
		var language = CurrentCode;
		if ( TryGetLanguagePhrases( language, out var phrases ) && phrases.TryGetValue( key, out var text ) )
			return text;

		if ( !string.Equals( language, EnglishCode, StringComparison.OrdinalIgnoreCase )
			&& TryGetLanguagePhrases( EnglishCode, out phrases )
			&& phrases.TryGetValue( key, out text ) )
			return text;

		return null;
	}

	private static bool TryGetLanguagePhrases( string code, out Dictionary<string, string> phrases )
	{
		var normalized = NormalizeLanguageCode( code );
		if ( PhrasesByLanguage.TryGetValue( normalized, out phrases ) )
			return true;

		phrases = normalized == RussianCode ? BuiltInRussian() : BuiltInEnglish();

		PhrasesByLanguage[normalized] = phrases;
		return phrases.Count > 0;
	}

	private static Dictionary<string, string> BuiltInEnglish() => BuiltInEnglishGenerated();

	private static Dictionary<string, string> BuiltInRussian() => BuiltInRussianGenerated();

	public static string Format( string key, string fallback, params object[] args )
	{
		return string.Format( CultureInfo.InvariantCulture, Phrase( key, fallback ), args ?? Array.Empty<object>() );
	}

	public static string JobHeader( JobDefinition job )
	{
		if ( job is null )
			return Phrase( "common.no_job", "No Job" );

		return Phrase( $"jobs.{NormalizeId( job.Id )}.header", job.Header );
	}

	public static string JobDescription( JobDefinition job )
	{
		if ( job is null )
			return string.Empty;

		return Phrase( $"jobs.{NormalizeId( job.Id )}.description", job.Description );
	}

	public static string ShopHeader( ShopDefinition shop )
	{
		if ( shop is null )
			return string.Empty;

		return Phrase( $"shop.{NormalizeId( shop.Id )}.header", shop.Header );
	}

	public static string ShopDescription( ShopDefinition shop )
	{
		if ( shop is null )
			return string.Empty;

		return Phrase( $"shop.{NormalizeId( shop.Id )}.description", shop.Description );
	}

	public static string ItemHeader( ItemDefinition item )
	{
		if ( item is null )
			return string.Empty;

		return Phrase( $"items.{NormalizeId( item.Id )}.header", item.Header );
	}

	public static string ItemDescription( ItemDefinition item )
	{
		if ( item is null )
			return string.Empty;

		return Phrase( $"items.{NormalizeId( item.Id )}.description", item.Description );
	}

	public static string Category( string category )
	{
		var normalized = NormalizeId( category );
		if ( string.IsNullOrWhiteSpace( normalized ) )
			normalized = "other";

		return Phrase( $"category.{normalized}", category );
	}

	private static string NormalizeLanguageCode( string code )
	{
		return string.Equals( code, RussianCode, StringComparison.OrdinalIgnoreCase )
			? RussianCode
			: EnglishCode;
	}

	private static string NormalizeId( string id )
	{
		return (id ?? string.Empty).Trim().ToLowerInvariant().Replace( " ", "_" );
	}
}
