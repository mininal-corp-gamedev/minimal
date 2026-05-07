namespace Minimal.Toolgun;

public sealed class RemoverTool : ToolMode
{
	public override string Id => "remover";
	public override string Title => "Remover";
	public override string Description => "Удаляет prop, который принадлежит тебе.";

	public override ToolUseResult Use(ToolUseContext context)
	{
		var prop = context.TargetProp;
		var propObject = prop.GameObject;
		var propName = !propObject.IsValid() || string.IsNullOrWhiteSpace(propObject.Name) ? "Prop" : propObject.Name;

		context.Player.UnregisterSpawnedProp(prop);
		propObject.Destroy();

		return ToolUseResult.Ok($"Удален prop: {propName}.");
	}
}
