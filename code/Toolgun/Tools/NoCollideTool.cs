namespace Minimal.Toolgun;

public sealed class NoCollideTool : ToolMode
{
	public const string NoCollidePlayerTag = "no_col_player";

	public override string Id => "nocollide";
	public override string Title => "No Collide";
	public override string Description => "Primary fire disables player collision, secondary fire restores it.";
	public override bool SupportsSecondary => true;

	public override ToolUseResult Use(ToolUseContext context)
	{
#if SERVER
		var propObject = context.TargetProp.GameObject;
		if (!propObject.Tags.Has(NoCollidePlayerTag))
			propObject.Tags.Add(NoCollidePlayerTag);

		return ToolUseResult.Ok(GameLocalization.Phrase( "notify.toolgun.nocollide_enabled", "No Collide enabled for prop." ));
#else
		return ToolUseResult.Fail(null);
#endif
	}

	public override ToolUseResult UseSecondary(ToolUseContext context)
	{
#if SERVER
		var propObject = context.TargetProp.GameObject;
		if (propObject.Tags.Has(NoCollidePlayerTag))
			propObject.Tags.Remove(NoCollidePlayerTag);

		return ToolUseResult.Ok(GameLocalization.Phrase( "notify.toolgun.nocollide_disabled", "No Collide disabled for prop." ));
#else
		return ToolUseResult.Fail(null);
#endif
	}
}
