using Sandbox;

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

    [Rpc.Host]
    private void Add(MoneyDropped money)
    {
        if (!Networking.IsHost) return;
        if (!GameObject.IsValid()) return;

        Money += money.Money;

        money.DestroyGameObject();
    }

    public bool Press(IPressable.Event e)
    {
        if (e.Source.GameObject.Components.TryGet<Player>(out var ply, FindMode.EverythingInSelfAndParent))
        {
            if (ply != PlayerOwner)
            {
                Notification.Make("Просто дропните деньги через С меню", 5f);

                return false;
            }

            Collect(ply);
        }

        return true;
    }

    [Rpc.Host]
    private void Collect(Player ply)
    {
        if (!Networking.IsHost) return;
        if (ply != PlayerOwner)
        {
            Rpc.Caller.Kick("[HoboTaxCollector] Try to collect money");

            return;
        }

        ply.Money += Money;
        Money = 0;
    }
}
