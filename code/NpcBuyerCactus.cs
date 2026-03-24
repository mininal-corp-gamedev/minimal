using Sandbox;

public sealed class NpcBuyerCactus : Component, Component.IPressable
{
    public bool Press(IPressable.Event e)
    {
        if (IsProxy) return false;

        var go = e.Source.GameObject;

        if (!go.Components.TryGet<Player>(out var ply, FindMode.EverythingInSelfAndParent)) return false;

        var cactuses = ply.CactusCount;
        if (cactuses <= 0) return false;

        ply.Money += 20 * cactuses;
        ply.CactusCount = 0;

        return true;
    }

    protected override void OnUpdate()
	{

	}
}
