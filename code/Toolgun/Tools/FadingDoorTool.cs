namespace Minimal.Toolgun;

public sealed class FadingDoorTool : ToolMode
{
	public override string Id => "fadingdoor";
	public override string Title => "Fading Door";
	public override string Description => "Primary fire adds Fading Door, secondary fire removes it.";
	public override bool SupportsSecondary => true;

	public override ToolUseResult Use( ToolUseContext context )
	{
#if SERVER
		var prop = context.TargetProp;
		if ( prop.HasFadingDoor )
			return ToolUseResult.Fail( GameLocalization.Phrase( "notify.toolgun.fading_door_already", "This prop already has a Fading Door." ) );

		prop.EnableFadingDoor( context.Player );
		return ToolUseResult.Ok( GameLocalization.Phrase( "notify.toolgun.fading_door_added", "Fading Door added." ) );
#else
		return ToolUseResult.Fail( null );
#endif
	}

	public override ToolUseResult UseSecondary( ToolUseContext context )
	{
#if SERVER
		var prop = context.TargetProp;
		if ( !prop.HasFadingDoor )
			return ToolUseResult.Fail( GameLocalization.Phrase( "notify.toolgun.fading_door_missing", "This prop has no Fading Door." ) );

		prop.DisableFadingDoor();
		return ToolUseResult.Ok( GameLocalization.Phrase( "notify.toolgun.fading_door_removed", "Fading Door removed." ) );
#else
		return ToolUseResult.Fail( null );
#endif
	}
}
