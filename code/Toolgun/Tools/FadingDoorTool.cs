namespace Minimal.Toolgun;

public sealed class FadingDoorTool : ToolMode
{
	public override string Id => "fadingdoor";
	public override string Title => "Fading Door";
	public override string Description => "Добавляет или удаляет Fading Door на твоём prop.";

	public override ToolUseResult Use(ToolUseContext context)
	{
		var gameObject = context.TargetProp.GameObject;
		if (!gameObject.Components.TryGet<global::FadingDoor>(out var door))
		{
			door = gameObject.Components.Create<global::FadingDoor>();
			door.SetOwner(context.Player);
			door.Close();
			return ToolUseResult.Ok("Fading Door добавлен.");
		}

		door.Close();
		door.Destroy();
		return ToolUseResult.Ok("Fading Door удален.");
	}
}
