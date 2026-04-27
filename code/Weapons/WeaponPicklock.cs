using Sandbox;

namespace Minimal.Weapons;

/// <summary>
/// Отмычка: ЛКМ — попытаться взломать заблокированную дверь перед собой.
/// Шанс успеха и длительность настраиваются на самой двери. Все важные проверки делает хост.
/// </summary>
public sealed class WeaponPicklock : Weapon
{
    [Property, Category("Combat")] public override bool IsMelee { get; set; } = true;
    [Property, Category("Reload")] public override bool HasReload { get; set; } = false;
    [Property, Category("Combat")] public override bool SemiAuto { get; set; } = true;
    [Property, Category("Combat")] public override float FireDelay { get; set; } = 0.45f;
    [Property, Category("Combat")] public override float Damage { get; set; } = 0f;
    [Property, Category("Combat")] public override float AttackRange { get; set; } = 100f;

    protected override void PerformFire()
    {
        if (!_timeUntilNextFire) return;

        _firedThisFrame = true;
        _timeUntilNextFire = FireDelay;

        var door = TraceForDoor();
        if (!door.IsValid()) return;
        if (!door.CanBeLockpicked()) return;

        door.RpcRequestLockpick();
    }

    /// <summary>Луч из глаз игрока вперёд на AttackRange. Возвращает первую найденную Door.</summary>
    private Door TraceForDoor()
    {
        if (!Player.Local.IsValid() || !Player.Local.Controller.IsValid())
            return null;

        var origin = Player.Local.Controller.EyeTransform.Position;
        var dir = Player.Local.Controller.EyeTransform.Forward;

        var tr = Scene.Trace
            .Ray(origin, origin + dir * AttackRange)
            .IgnoreGameObjectHierarchy(Player.Local.GameObject)
            .WithoutTags("bullet")
            .Run();

        if (!tr.Hit) return null;

        var go = tr.GameObject;
        while (go.IsValid())
        {
            if (go.Components.TryGet<Door>(out var d, FindMode.EverythingInSelfAndParent))
                return d;
            go = go.Parent;
        }

        return null;
    }
}
