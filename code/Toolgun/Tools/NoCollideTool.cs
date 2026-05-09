namespace Minimal.Toolgun;

public sealed class NoCollideTool : ToolMode
{
	public const string NoCollidePlayerTag = "no_col_player";

	public override string Id => "nocollide";
	public override string Title => "No Collide";
	public override string Description => "ЛКМ отключает collision с игроками, ПКМ возвращает collision.";
	public override bool SupportsSecondary => true;

	public override ToolUseResult Use(ToolUseContext context)
	{
		var propObject = context.TargetProp.GameObject;
		if (!propObject.Tags.Has(NoCollidePlayerTag))
			propObject.Tags.Add(NoCollidePlayerTag);

		return ToolUseResult.Ok("No Collide включен для prop.");
	}

	public override ToolUseResult UseSecondary(ToolUseContext context)
	{
		var propObject = context.TargetProp.GameObject;
		if (propObject.Tags.Has(NoCollidePlayerTag))
			propObject.Tags.Remove(NoCollidePlayerTag);

		return ToolUseResult.Ok("No Collide выключен для prop.");
	}
}
