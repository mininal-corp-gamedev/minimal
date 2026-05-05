using Sandbox;

public sealed class MoneyPrinterUpgrade : Component, Component.ICollisionListener
{
    [Property, Sync(SyncFlags.FromHost)] public int MoneyPerTick { get; set; } = 5;
    [Property, Sync(SyncFlags.FromHost)] public float TickInterval { get; set; } = 0.1f;

    void ICollisionListener.OnCollisionStart(Collision collision)
    {
        var obj = collision.Other.GameObject;

        if (obj.Components.TryGet<MoneyPrinterBase>(out var printer, FindMode.EverythingInSelfAndParent))
        {
            UpgradePrinter(printer);
        }
    }

    [Rpc.Host]
    private void UpgradePrinter(MoneyPrinterBase printer)
    {
        if (!Networking.IsHost) return;
        if (!GameObject.IsValid()) return;

        printer.MoneyPerTick += MoneyPerTick;
        printer.TickInterval -= TickInterval;

        GameObject.Destroy();
    }
}
