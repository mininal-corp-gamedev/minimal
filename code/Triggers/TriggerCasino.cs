using Sandbox;

public sealed class TriggerCasino : Component, Component.ITriggerListener
{
    void ITriggerListener.OnTriggerEnter(GameObject other)
    {
        if (!other.Components.TryGet<Player>(out var player, FindMode.EverythingInSelfAndParent)) return;

        ChangeCasinoRpc(player);
    }

    void ITriggerListener.OnTriggerExit(GameObject other)
    {
        if (!other.Components.TryGet<Player>(out var player, FindMode.EverythingInSelfAndParent)) return;

        ChangeCasinoRpc(player);
    }

    [Rpc.Host]
    private void ChangeCasinoRpc(Player ply)
    {
#if SERVER
        if (!Networking.IsHost) return;
        if (ply.Network.Owner != Rpc.Caller) return;

        ply.IsCasino = !ply.IsCasino;

        //Log.Info($"[Casino] {Rpc.Caller.DisplayName} ({Rpc.Caller.SteamId}) - {ply.IsCasino}");
#endif
    }
}
