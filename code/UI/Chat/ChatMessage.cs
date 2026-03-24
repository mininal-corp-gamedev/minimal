using Sandbox;
using System;

namespace Sandbox;

public sealed class ChatMessage
{
	public string SenderName { get; init; }
	public string Text { get; init; }
	public bool IsSystem { get; init; }
	public ulong? SteamId { get; init; }
	public DateTime SentAt { get; init; }
	public double CreatedAtSeconds { get; init; }

	public static ChatMessage CreatePlayer( string senderName, ulong? steamId, string text )
	{
		return new ChatMessage
		{
			SenderName = string.IsNullOrWhiteSpace( senderName ) ? "Player" : senderName.Trim(),
			Text = text,
			IsSystem = false,
			SteamId = steamId,
			SentAt = DateTime.Now,
			CreatedAtSeconds = Time.Now
		};
	}

	public static ChatMessage CreateSystem( string text )
	{
		return new ChatMessage
		{
			SenderName = "System",
			Text = text,
			IsSystem = true,
			SteamId = null,
			SentAt = DateTime.Now,
			CreatedAtSeconds = Time.Now
		};
	}

	public int BuildHash()
	{
		return HashCode.Combine( SenderName, Text, IsSystem, SteamId, SentAt );
	}
}
