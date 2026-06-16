using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Minimal.Clan;

public static partial class ClanDatabase
{
	public const string SaveFolder = "clans";
	public const string SavePath = $"{SaveFolder}/clans.json";
}

public sealed class ClanDatabaseSave
{
	[JsonPropertyName( "clans" )] public List<Clan> Clans { get; set; } = new();
}
