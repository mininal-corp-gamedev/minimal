using Sandbox;
using System;

public sealed class MoneyPrinterUpgrade : Component, Component.ICollisionListener
{
    [Property, Sync(SyncFlags.FromHost)] public int MoneyPerTick { get; set; } = 5;
    [Property, Sync(SyncFlags.FromHost)] public float TickInterval { get; set; } = 0.1f;

    void ICollisionListener.OnCollisionStart(Collision collision)
    {
#if SERVER
        if (!Networking.IsHost) return;

        var obj = collision.Other.GameObject;

        if (obj.Components.TryGet<MoneyPrinterBase>(out var printer, FindMode.EverythingInSelfAndParent))
        {
            UpgradePrinter(printer);
        }
#endif
    }

#if SERVER
    private void UpgradePrinter(MoneyPrinterBase printer)
    {
        if (!Networking.IsHost) return;
        if (!GameObject.IsValid()) return;
        if (!printer.IsValid() || !printer.GameObject.IsValid()) return;
        if (Vector3.DistanceBetween(WorldPosition, printer.WorldPosition) > 140f) return;

        printer.MoneyPerTick += MoneyPerTick;
        printer.TickInterval = MathF.Max(0.05f, printer.TickInterval - TickInterval);

        GameObject.Destroy();
    }
#endif
}
