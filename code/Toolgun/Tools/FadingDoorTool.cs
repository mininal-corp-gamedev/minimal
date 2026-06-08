namespace Minimal.Toolgun;

public sealed class FadingDoorTool : ToolMode
{
	public override string Id => "fadingdoor";
	public override string Title => "Fading Door";
	public override string Description => "Adds or removes Fading Door on your prop.";

	public override ToolUseResult Use( ToolUseContext context )
	{
#if SERVER
		var prop = context.TargetProp;
		if ( !prop.HasFadingDoor )
		{
			prop.EnableFadingDoor( context.Player );
			return ToolUseResult.Ok( GameLocalization.Phrase( "notify.toolgun.fading_door_added", "Fading Door added." ) );
		}

		prop.DisableFadingDoor();
		return ToolUseResult.Ok( GameLocalization.Phrase( "notify.toolgun.fading_door_removed", "Fading Door removed." ) );
#else
		return ToolUseResult.Fail( null );
#endif
	}
}
