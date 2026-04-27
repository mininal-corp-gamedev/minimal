using Sandbox;

namespace Minimal.Weapons;

/// <summary>
/// Наручники: ЛКМ — арестовать игрока перед собой, ПКМ — выпустить из ареста.
/// Все важные проверки делает хост (server authority).
/// </summary>
public sealed class WeaponHandcuff : Weapon
{
    [Property, Category("Combat")] public override bool IsMelee { get; set; } = true;
    [Property, Category("Reload")] public override bool HasReload { get; set; } = false;
    [Property, Category("Combat")] public override bool SemiAuto { get; set; } = true;
    [Property, Category("Combat")] public override float FireDelay { get; set; } = 0.45f;
    [Property, Category("Combat")] public override float Damage { get; set; } = 0f;
    [Property, Category("Combat")] public override float AttackRange { get; set; } = 90f;

    protected override void PerformFire()
    {
        if (!_timeUntilNextFire) return;

        _firedThisFrame = true;
        _timeUntilNextFire = FireDelay;

        var target = TraceForPlayer();
        if (!target.IsValid()) return;
        if (target == Player.Local) return;
        if (target.IsArrested) return;

        Player.Local?.RequestArrestTarget(target.GameObject);
    }

    protected override void OnSecondaryAttack()
    {
        if (!_timeUntilNextFire) return;
        _timeUntilNextFire = FireDelay;

        var target = TraceForPlayer();
        if (!target.IsValid()) return;
        if (target == Player.Local) return;
        if (!target.IsArrested) return;

        Player.Local?.RequestReleaseTarget(target.GameObject);
    }

    /// <summary>Луч из глаз игрока вперёд на AttackRange. Возвращает первого найденного Player.</summary>
    private Player TraceForPlayer()
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
            if (go.Components.TryGet<Player>(out var p, FindMode.EverythingInSelfAndParent))
                return p;
            go = go.Parent;
        }

        return null;
    }
}
