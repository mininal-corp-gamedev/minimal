using Ambi.Utils;
using Sandbox;

/// <summary>
/// Temporary first quest giver. Attach this component to a networked cube with a collider.
/// The visual model can be replaced later without changing the quest logic.
/// </summary>
public sealed partial class QuestNpcMark : Component, Component.IPressable, ICustomDamagable
{
	public const string DefaultQuestId = "mark_intro";

	[Property, Description( "Quest given by Mark when the player interacts for the first time." )]
	public string QuestId { get; set; } = DefaultQuestId;

	[Property, Description( "Maximum distance at which the player may talk to Mark." )]
	public float MaxInteractDistance { get; set; } = 140f;

	[Property, Description( "Maximum validated melee distance for the training punches." )]
	public float MaxHitDistance { get; set; } = 105f;

	public bool Press( IPressable.Event e )
	{
		var source = e.Source?.GameObject;
		if ( !source.IsValid() ) return false;

		if ( !source.Components.TryGet<Player>( out var player, FindMode.EverythingInSelfAndParent ) )
			return false;

		// Only the owning client sends the request. The host validates it again.
		if ( player.IsProxy ) return false;

		RpcRequestInteract();
		return true;
	}

	public void OnDamage( in DamageInfo damage )
	{
		var localPlayer = Player.Local;
		if ( !localPlayer.IsValid() || !localPlayer.IsAlive ) return;
		if ( !string.Equals( localPlayer.CurrentWeaponItemId, "hands", System.StringComparison.OrdinalIgnoreCase ) ) return;

		if ( !damage.Attacker.IsValid() ) return;
		if ( !damage.Attacker.Components.TryGet<Player>( out var attacker, FindMode.EverythingInSelfAndParent ) ) return;
		if ( attacker != localPlayer ) return;

		var origin = damage.Origin;
		var direction = damage.Position - damage.Origin;
		if ( direction.LengthSquared <= 0.001f && localPlayer.Controller.IsValid() )
		{
			origin = localPlayer.Controller.EyePosition;
			direction = localPlayer.Controller.EyeTransform.Forward;
		}

		if ( direction.LengthSquared <= 0.001f ) return;
		RpcRequestTrainingHit( origin, direction.Normal );
	}

	[Rpc.Host]
	private void RpcRequestInteract() => RequestInteractServer();

	public void RequestDialogueAction( MarkDialogueAction action )
	{
		if ( action == MarkDialogueAction.None ) return;
		RpcRequestDialogueAction( (int)action );
	}

	[Rpc.Host]
	private void RpcRequestDialogueAction( int action ) => RequestDialogueActionServer( action );

	[Rpc.Host]
	private void RpcRequestTrainingHit( Vector3 origin, Vector3 direction ) => RequestTrainingHitServer( origin, direction );

	partial void RequestInteractServer();
	partial void RequestDialogueActionServer( int action );
	partial void RequestTrainingHitServer( Vector3 origin, Vector3 direction );

	[Rpc.Broadcast]
	private void RpcOpenDialogue( int state, int currentCount, int requiredCount )
	{
		QuestDialoguePanel.Open( this, (MarkDialogueState)state, currentCount, requiredCount );
	}
}

public enum MarkDialogueState
{
	Introduction,
	QuestStarted,
	HitObjective,
	DoorsObjective,
	SpawnPropObjective,
	PhysgunObjective,
	RemovePropObjective,
	ReturnReady,
	Completed,
	AlreadyFinished,
	Unavailable
}

public enum MarkDialogueAction
{
	None,
	AcceptQuest,
	TurnInQuest
}
