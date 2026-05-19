using Sandbox;
using System;
using System.Collections.Generic;

public sealed class BulletPassThrough : Component
{
}

public static class BulletCollisionRules
{
	public const string DefaultPassThroughTag = "bullet_passthrough";

	public static List<string> CreateDefaultPassThroughTags()
	{
		return new List<string> { DefaultPassThroughTag };
	}

	public static List<string> CloneTags( IReadOnlyList<string> tags )
	{
		var result = new List<string>();

		if ( tags is not null )
		{
			foreach ( var tag in tags )
			{
				if ( string.IsNullOrWhiteSpace( tag ) )
					continue;

				result.Add( tag.Trim() );
			}
		}

		if ( result.Count == 0 )
			result.Add( DefaultPassThroughTag );

		return result;
	}

	public static GameObject GetHitObject( SceneTraceResult trace )
	{
		if ( trace.GameObject.IsValid() )
			return trace.GameObject;

		if ( trace.Component.IsValid() )
			return trace.Component.GameObject;

		return null;
	}

	public static bool TryGetPassThroughRoot( GameObject hitObject, IReadOnlyList<string> passThroughTags, out GameObject root )
	{
		root = null;

		var current = hitObject;
		while ( current.IsValid() )
		{
			if ( current.Components.TryGet<BulletPassThrough>( out _, FindMode.EverythingInSelf ) )
			{
				root = current;
				return true;
			}

			if ( current.Components.TryGet<Glass>( out var glass, FindMode.EverythingInSelf ) && glass.AllowBulletPassThrough )
			{
				root = current;
				return true;
			}

			if ( HasAnyTag( current, passThroughTags ) )
			{
				root = current;
				return true;
			}

			current = current.Parent;
		}

		return false;
	}

	private static bool HasAnyTag( GameObject gameObject, IReadOnlyList<string> tags )
	{
		if ( !gameObject.IsValid() || tags is null )
			return false;

		foreach ( var tag in tags )
		{
			if ( string.IsNullOrWhiteSpace( tag ) )
				continue;

			if ( gameObject.Tags.Has( tag.Trim() ) )
				return true;
		}

		return false;
	}
}
