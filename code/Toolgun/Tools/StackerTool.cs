using Sandbox;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Minimal.Toolgun;

public sealed class StackerTool : ToolMode
{
	public const string SideConfigKey = "stacker.side";
	public const string CountConfigKey = "stacker.count";
	public const string GapConfigKey = "stacker.gap";

	public const string SideUp = "up";
	public const string SideDown = "down";
	public const string SideLeft = "left";
	public const string SideRight = "right";
	public const string SideFront = "front";
	public const string SideBack = "back";

	public const int MaxCount = 10;
	public const float MaxGap = 100f;

	private static readonly IReadOnlyList<ToolConfigButton> SideButtons = new[]
	{
		new ToolConfigButton { Label = "Up", Value = SideUp },
		new ToolConfigButton { Label = "Down", Value = SideDown },
		new ToolConfigButton { Label = "Left", Value = SideLeft },
		new ToolConfigButton { Label = "Right", Value = SideRight },
		new ToolConfigButton { Label = "Front", Value = SideFront },
		new ToolConfigButton { Label = "Back", Value = SideBack }
	};

	private static readonly IReadOnlyList<ToolConfigField> Fields = new[]
	{
		new ToolConfigField
		{
			Id = SideConfigKey,
			Label = "Side",
			Type = ToolConfigType.Button,
			Buttons = SideButtons,
			DefaultValue = SideUp
		},
		new ToolConfigField
		{
			Id = CountConfigKey,
			Label = "Count",
			Type = ToolConfigType.Slider,
			Min = 1f,
			Max = MaxCount,
			Step = 1f,
			DefaultValue = "1"
		},
		new ToolConfigField
		{
			Id = GapConfigKey,
			Label = "Gap",
			Type = ToolConfigType.Slider,
			Min = 0f,
			Max = MaxGap,
			Step = 0.25f,
			DefaultValue = "1"
		}
	};

	public override string Id => "stacker";
	public override string Title => "Stacker";
	public override string Description => "Stacks copies of your prop in the selected direction.";
	public override IReadOnlyList<ToolConfigField> ConfigFields => Fields;

	public override ToolUseResult Use( ToolUseContext context )
	{
#if SERVER
		var player = context.Player;
		var sourceProp = context.TargetProp;
		var sourceObject = sourceProp.GameObject;

		if ( player.Job?.JobDefinition?.CanSpawnProp == false )
			return ToolUseResult.Fail( GameLocalization.Phrase( "notify.props.job_cannot_spawn", "Your job cannot spawn props." ) );

		var model = GetPropModel( sourceObject );
		if ( model is null || model.IsError )
			return ToolUseResult.Fail( GameLocalization.Phrase( "notify.toolgun.stacker_no_model", "This prop has no model." ) );

		var side = ParseSide( context.Config );
		var count = ParseCount( context.Config );
		var gap = ParseGap( context.Config );

		if ( !TryGetPlacements( sourceObject, side, count, gap, out var placements ) )
			return ToolUseResult.Fail( null );

		var sourceRb = sourceObject.Components.Get<Rigidbody>( FindMode.EverythingInSelfAndDescendants );
		var massOverride = sourceRb.IsValid() ? sourceRb.MassOverride : 0f;
		var tint = sourceProp.PropTint;
		var scale = sourceObject.WorldScale;

		var spawned = 0;
		foreach ( var placement in placements )
		{
			if ( player.OwnedPropsCount >= player.MaxProps )
				break;

			var go = new GameObject( true, sourceObject.Name );
			go.WorldPosition = placement.Position;
			go.WorldRotation = placement.Rotation;
			go.WorldScale = scale;

			var propComponent = go.Components.Create<Sandbox.Prop>();
			propComponent.Model = model;
			propComponent.Health = 999999f;

			var propCustom = go.Components.Create<PropCustom>();
			propCustom.SetOwner( player );
			propCustom.ConfigureInitialPhysics( PropPhysicsMode.Frozen, massOverride );
			propCustom.SetTint( tint );
			player.RegisterSpawnedProp( propCustom );

			go.Tags.Add( "prop" );
			go.NetworkSpawn();
			OwnedPropNetwork.ConfigurePropCustom( go );
			PropCollisionTags.RefreshPhysicsShapeTags( go );

			spawned++;
		}

		if ( spawned == 0 )
			return ToolUseResult.Fail( GameLocalization.Format( "notify.props.limit_reached", "Prop limit reached ({0}).", player.MaxProps ) );

		MarkIntroQuest.TryAdvance( player, MarkIntroQuest.SpawnPropTaskId );

		if ( spawned < count )
			return ToolUseResult.Ok( GameLocalization.Format( "notify.toolgun.stacker_created_partial", "Stacked {0} of {1} props (prop limit).", spawned, count ) );

		return ToolUseResult.Ok( GameLocalization.Format( "notify.toolgun.stacker_created", "Stacked {0} props.", spawned ) );
#else
		return ToolUseResult.Fail( null );
#endif
	}

	public static string ParseSide( IReadOnlyDictionary<string, string> config )
	{
		var raw = config.TryGetValue( SideConfigKey, out var value ) ? value : SideUp;
		return raw switch
		{
			SideUp or SideDown or SideLeft or SideRight or SideFront or SideBack => raw,
			_ => SideUp
		};
	}

	public static int ParseCount( IReadOnlyDictionary<string, string> config )
	{
		var raw = config.TryGetValue( CountConfigKey, out var value ) ? value : "1";
		if ( !int.TryParse( raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count ) )
			count = 1;
		return Math.Clamp( count, 1, MaxCount );
	}

	public static float ParseGap( IReadOnlyDictionary<string, string> config )
	{
		var raw = config.TryGetValue( GapConfigKey, out var value ) ? value : "1";
		if ( !float.TryParse( raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var gap ) )
			gap = 1f;
		return Math.Clamp( gap, 0f, MaxGap );
	}

	public static Vector3 GetSideDirection( string side, Rotation rotation )
	{
		return side switch
		{
			SideDown => rotation * Vector3.Down,
			SideLeft => rotation * Vector3.Left,
			SideRight => rotation * Vector3.Right,
			SideFront => rotation * Vector3.Forward,
			SideBack => rotation * Vector3.Backward,
			_ => rotation * Vector3.Up
		};
	}

	private static float GetSizeAlongAxis( BBox bounds, string side, Vector3 scale )
	{
		return side switch
		{
			SideUp or SideDown => (bounds.Maxs.z - bounds.Mins.z) * scale.z,
			SideLeft or SideRight => (bounds.Maxs.x - bounds.Mins.x) * scale.x,
			SideFront or SideBack => (bounds.Maxs.y - bounds.Mins.y) * scale.y,
			_ => (bounds.Maxs.z - bounds.Mins.z) * scale.z
		};
	}

	public static bool TryGetPlacements( GameObject propObject, string side, int count, float gap, out List<Transform> placements )
	{
		placements = new List<Transform>();

		if ( !propObject.IsValid() )
			return false;

		var model = GetPropModel( propObject );
		var bounds = model is not null && !model.IsError
			? model.Bounds
			: propObject.GetLocalBounds();
		var scale = propObject.WorldScale;
		var sizeAlongAxis = GetSizeAlongAxis( bounds, side, scale );
		if ( sizeAlongAxis <= 0f )
			sizeAlongAxis = 1f;

		var direction = GetSideDirection( side, propObject.WorldRotation );
		var step = sizeAlongAxis + gap;
		var rotation = propObject.WorldRotation;
		var position = propObject.WorldPosition;

		for ( var i = 1; i <= count; i++ )
		{
			var placementPos = position + direction * step * i;
			placements.Add( new Transform( placementPos, rotation, scale ) );
		}

		return placements.Count > 0;
	}

	public static Model GetPropModel( GameObject obj )
	{
		if ( !obj.IsValid() )
			return null;

		var prop = obj.Components.Get<Sandbox.Prop>( FindMode.EverythingInSelfAndAncestors );
		if ( prop.IsValid() && prop.Model is not null && !prop.Model.IsError )
			return prop.Model;

		var renderer = obj.Components.Get<ModelRenderer>( FindMode.EverythingInSelfAndAncestors );
		if ( renderer.IsValid() && renderer.Model is not null && !renderer.Model.IsError )
			return renderer.Model;

		return null;
	}
}
