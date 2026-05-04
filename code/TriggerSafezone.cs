using Sandbox;

public sealed class TriggerSafezone : Component, Component.ITriggerListener
{
    void ITriggerListener.OnTriggerEnter(GameObject other)
    {
        if (!other.Components.TryGet<Player>(out var player, FindMode.EverythingInSelfAndParent)) return;

        ChangeSafezoneRpc(player);
    }

    void ITriggerListener.OnTriggerExit(GameObject other)
    {
        if (!other.Components.TryGet<Player>(out var player, FindMode.EverythingInSelfAndParent)) return;

        ChangeSafezoneRpc(player);
    }

    [Rpc.Host]
    private void ChangeSafezoneRpc(Player ply)
    {
        if (!Networking.IsHost) return;
        if (ply.Network.Owner != Rpc.Caller) return;

        ply.IsSafezone = !ply.IsSafezone;

        Log.Info($"[Safezone] {Rpc.Caller.DisplayName} ({Rpc.Caller.SteamId}) - {ply.IsSafezone}");
    }
}
