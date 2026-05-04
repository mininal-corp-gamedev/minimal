using Sandbox;

public sealed class WeaponWorldModel : Component
{
	[Property] public Vector3 PositionOffset { get; set; } = Vector3.Zero;
	[Property] public Rotation RotationOffset { get; set; } = Rotation.Identity;
	[Property] public float Scale { get; set; } = 1f;
	/// <summary>Смещение точки начала луча от кости hold_r. Используется для physgun-подобного оружия.</summary>
	[Property] public Vector3 BeamStartOffset { get; set; } = Vector3.Zero;
}
