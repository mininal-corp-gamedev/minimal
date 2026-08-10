using Sandbox;
using System.Collections.Generic;

namespace Minimal.Toolgun;

/// <summary>
/// Client-only ghost preview for the Stacker tool.
/// Shows transparent copies of the aimed prop in the configured direction/count/gap.
/// No networking, no physics, no ownership — purely visual.
/// </summary>
public sealed class StackerGhost : Component
{
	private static StackerGhost _instance;

	private readonly List<GameObject> _ghosts = new();
	private GameObject _lastTarget;
	private Model _lastModel;
	private Color _lastTint;
	private int _lastCount;
	private bool _active;

	/// <summary>Called every frame from WeaponToolgun.OnWeaponUpdate.</summary>
	public static void Update()
	{
		var local = Player.Local;
		if ( !local.IsValid() || !local.Controller.IsValid() )
		{
			Clear();
			return;
		}

		var tool = ToolgunClientState.SelectedTool;
		if ( tool is not StackerTool )
		{
			Clear();
			return;
		}

		if ( local.IsArrested || !local.IsAlive )
		{
			Clear();
			return;
		}

		EnsureInstance();

		if ( _instance.IsValid() )
			_instance.UpdateGhosts( local );
	}

	public static void Clear()
	{
		if ( _instance.IsValid() )
			_instance.ClearGhosts();
	}

	private static void EnsureInstance()
	{
		if ( _instance.IsValid() )
			return;

		var go = new GameObject( true, "StackerGhost" );
		_instance = go.Components.Create<StackerGhost>();
	}

	protected override void OnDestroy()
	{
		ClearGhosts();

		if ( _instance == this )
			_instance = null;
	}

	private void UpdateGhosts( Player local )
	{
		var eye = local.Controller.EyeTransform;
		var trace = local.Scene.Trace
			.Ray( eye.Position, eye.Position + eye.Forward * 420f )
			.IgnoreGameObjectHierarchy( local.GameObject )
			.WithoutTags( "player", "bullet" )
			.Run();

		GameObject targetObject = null;
		PropCustom targetProp = null;

		if ( trace.Hit && trace.GameObject.IsValid() )
		{
			targetProp = trace.GameObject.Components.Get<PropCustom>( FindMode.EverythingInSelfAndAncestors );
			if ( targetProp.IsValid() )
				targetObject = targetProp.GameObject;
		}

		if ( targetObject is null || !targetObject.IsValid() )
		{
			ClearGhosts();
			return;
		}

		var model = StackerTool.GetPropModel( targetObject );
		if ( model is null || model.IsError )
		{
			ClearGhosts();
			return;
		}

		var config = ToolgunClientState.GetConfigJson();
		var parsed = ToolMode.ParseConfig( config );
		var side = StackerTool.ParseSide( parsed );
		var count = StackerTool.ParseCount( parsed );
		var gap = StackerTool.ParseGap( parsed );

		if ( !StackerTool.TryGetPlacements( targetObject, side, count, gap, out var placements ) )
		{
			ClearGhosts();
			return;
		}

		var tint = targetProp.PropTint;
		tint = tint.WithAlpha( 0.4f );

		var needsRebuild = _lastTarget != targetObject
			|| _lastModel != model
			|| _lastCount != count
			|| _ghosts.Count != placements.Count;

		if ( needsRebuild )
		{
			ClearGhosts();
			_lastTarget = targetObject;
			_lastModel = model;
			_lastCount = count;
			_lastTint = tint;

			foreach ( var placement in placements )
			{
				var ghostGo = new GameObject( true, "StackerGhostItem" );
				ghostGo.WorldPosition = placement.Position;
				ghostGo.WorldRotation = placement.Rotation;
				ghostGo.WorldScale = placement.Scale;

				var renderer = ghostGo.Components.Create<ModelRenderer>();
				renderer.Model = model;
				renderer.Tint = tint;

				if ( renderer.SceneObject is not null )
					renderer.SceneObject.Flags.CastShadows = false;

				_ghosts.Add( ghostGo );
			}

			_active = true;
		}
		else
		{
			for ( var i = 0; i < _ghosts.Count && i < placements.Count; i++ )
			{
				var ghostGo = _ghosts[i];
				if ( !ghostGo.IsValid() )
					continue;

				ghostGo.WorldPosition = placements[i].Position;
				ghostGo.WorldRotation = placements[i].Rotation;
				ghostGo.WorldScale = placements[i].Scale;
			}

			if ( _lastTint != tint )
			{
				_lastTint = tint;
				foreach ( var ghostGo in _ghosts )
				{
					if ( !ghostGo.IsValid() )
						continue;

					var renderer = ghostGo.Components.Get<ModelRenderer>();
					if ( renderer.IsValid() )
						renderer.Tint = tint;
				}
			}
		}
	}

	private void ClearGhosts()
	{
		if ( !_active && _ghosts.Count == 0 )
			return;

		foreach ( var ghostGo in _ghosts )
		{
			if ( ghostGo.IsValid() )
				ghostGo.Destroy();
		}

		_ghosts.Clear();
		_lastTarget = null;
		_lastModel = null;
		_lastTint = Color.White;
		_lastCount = 0;
		_active = false;
	}
}
