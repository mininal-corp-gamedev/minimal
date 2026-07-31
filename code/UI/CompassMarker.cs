using Sandbox;

/// <summary>
/// Marks a world object as a destination on the player's compass.
/// This component is intentionally independent from quests and NPCs.
/// </summary>
public sealed class CompassMarker : Component
{
	[Property, Description( "Text displayed below the compass marker." )]
	public string Label { get; set; } = "Objective";

	[Property, Description( "Show the approximate distance from the local player." )]
	public bool ShowDistance { get; set; } = true;
}
