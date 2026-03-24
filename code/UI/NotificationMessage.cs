using System;

namespace Sandbox;

public sealed class NotificationMessage
{
	public int Id { get; init; }
	public string Text { get; init; }
	public NotificationType Type { get; init; }
	public bool IsLeaving { get; set; }
	public double CreatedAtSeconds { get; init; }
	public float AliveSeconds { get; init; }

	public int BuildHash()
	{
		return HashCode.Combine( Id, Text, Type, IsLeaving, CreatedAtSeconds, AliveSeconds );
	}
}

public enum NotificationType
{
	Info,
	Warn,
	Error
}
