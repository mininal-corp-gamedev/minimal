using Sandbox;
using System;

public sealed class HoboTaxCollector : Component, Component.IPressable, Component.ICollisionListener
{
    [Sync(SyncFlags.FromHost)] public Player PlayerOwner { get; set; }
    [Sync(SyncFlags.FromHost)] public int Money { get; set; } = 0;

    void ICollisionListener.OnCollisionStart(Collision collision)
    {
        var obj = collision.Other.GameObject;

        if (obj.Components.TryGet<MoneyDropped>(out var money, FindMode.EverythingInSelfAndParent))
        {
            Add(money);
        }
    }

    private void Add(MoneyDropped money)
    {
        if (!Networking.IsHost) return;
        if (!GameObject.IsValid()) return;
        if (!money.IsValid() || !money.GameObject.IsValid()) return;
        if (money.Money <= 0) return;
        if (Vector3.DistanceBetween(WorldPosition, money.WorldPosition) > 140f) return;

        Money = (int)Math.Clamp((long)Money + money.Money, 0L, int.MaxValue);
        money.Money = 0;

        money.DestroyGameObject();
    }

    public bool Press(IPressable.Event e)
    {
        if (e.Source.GameObject.Components.TryGet<Player>(out var ply, FindMode.EverythingInSelfAndParent))
        {
            if (ply != PlayerOwner)
            {
                Notification.Make(GameLocalization.Phrase( "notify.hobo_collector.drop_money_hint", "Drop money through the C menu." ), 5f);

                return false;
            }

            if (Networking.IsHost)
                HostCollect(ply, ply.GameObject.Network.Owner);
            else
                RpcCollect();
        }

        return true;
    }

    [Rpc.Host]
    private void RpcCollect()
    {
        if (!Networking.IsHost) return;

        var caller = Rpc.Caller;
        if (caller is null) return;

        var player = Player.FindPlayerBySteamId(caller.SteamId.Value);
        if (!player.IsValid() || player.GameObject.Network.Owner != caller)
            return;

        HostCollect(player, caller);
    }

    private void HostCollect(Player player, Connection caller)
    {
        if (!Networking.IsHost) return;
        if (!player.IsValid()) return;

        if (player != PlayerOwner)
        {
            caller?.Kick("[HoboTaxCollector] Try to collect money");

            return;
        }

        var ownerConnection = PlayerOwner.GameObject.Network.Owner;
        if (caller is not null && ownerConnection != caller)
            return;

        if (Money <= 0)
            return;

        player.Money = (int)Math.Clamp((long)player.Money + Money, 0L, int.MaxValue);
        Money = 0;
    }
}
