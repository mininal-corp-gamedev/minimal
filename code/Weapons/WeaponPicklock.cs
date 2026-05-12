using Sandbox;

namespace Minimal.Weapons;

/// <summary>
/// Отмычка: ЛКМ — начать мини-игру взлома для IDoorHackable перед собой.
/// Все важные проверки и результат применяет хост.
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

        var hackable = TraceForHackable();
        if (hackable is null) return;

        hackable.RequestDoorHack();
    }

    /// <summary>Луч из глаз игрока вперёд на AttackRange. Возвращает первый IDoorHackable.</summary>
    private IDoorHackable TraceForHackable()
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

        if (!DoorHackSystem.TryGetHackable(tr.GameObject, out var hackable))
            return null;

        return hackable;
    }
}
