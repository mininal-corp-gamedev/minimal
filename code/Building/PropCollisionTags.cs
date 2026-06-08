using Minimal.Toolgun;
using Sandbox;

public static class PropCollisionTags
{
	public const string PropTag = "prop";
	public const string PhysgunHeldTag = "physgun_held";

	public static void ApplyNoCollideTag( GameObject gameObject, bool enabled )
	{
		if ( !gameObject.IsValid() )
			return;

		if ( enabled )
			gameObject.Tags.Add( NoCollideTool.NoCollidePlayerTag );
		else
			gameObject.Tags.Remove( NoCollideTool.NoCollidePlayerTag );

		RefreshPhysicsShapeTags( gameObject );
	}

	public static void RefreshPhysicsShapeTags( GameObject gameObject )
	{
		TryRefreshPhysicsShapeTags( gameObject );
	}

	public static bool TryRefreshPhysicsShapeTags( GameObject gameObject )
	{
		return TryRefreshPhysicsShapeTags( gameObject, out _ );
	}

	public static bool TryRefreshPhysicsShapeTags( GameObject gameObject, out int shapeCount )
	{
		if ( !gameObject.IsValid() )
		{
			shapeCount = 0;
			return false;
		}

		var rb = gameObject.Components.Get<Rigidbody>( FindMode.EverythingInSelfAndDescendants );
		if ( !rb.IsValid() || rb.PhysicsBody is null || !rb.PhysicsBody.IsValid() )
		{
			shapeCount = 0;
			return false;
		}

		shapeCount = 0;
		foreach ( var shape in rb.PhysicsBody.Shapes )
		{
			if ( !shape.IsValid() )
				continue;

			shape.Tags.SetFrom( gameObject.Tags );
			shapeCount++;
		}

		return shapeCount > 0;
	}
}
