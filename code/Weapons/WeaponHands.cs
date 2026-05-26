using Sandbox;

namespace Minimal.Weapons;

/// <summary>
/// Кирка / ближний бой: без патронов и перезарядки, удар лучом без снарядов.
/// </summary>
public sealed class WeaponHands : Weapon
{
    private WeaponHandedness _attackHand = WeaponHandedness.LeftHand;

    [Property, Category("Combat")] public override bool IsMelee { get; set; } = true;
    [Property, Category("Reload")] public override bool HasReload { get; set; } = false;
    [Property, Category("Combat")] public override bool SemiAuto { get; set; } = true;
    [Property, Category("Combat")] public override float FireDelay { get; set; } = 0.45f;
    [Property, Category("Combat")] public override float Damage { get; set; } = 25f;
    [Property, Category("Combat")] public override float AttackRange { get; set; } = 80f;

    protected override int GetHoldTypeAttack() => (int)_attackHand;

    protected override void ResetViewmodelState()
    {
        _attackHand = WeaponHandedness.BothHands;
        base.ResetViewmodelState();
    }

    protected override void HandleDefaultCombatInput()
    {
        var attackLeft = SemiAuto ? Input.Pressed("Attack1") : Input.Down("Attack1");
        var attackRight = SemiAuto ? Input.Pressed("Attack2") : Input.Down("Attack2");

        if (attackLeft)
            AttackWithHand(WeaponHandedness.LeftHand);
        else if (attackRight)
            AttackWithHand(WeaponHandedness.RightHand);

        IsIronSight = false;
    }

    protected override void PerformFire()
    {
        AttackWithHand(WeaponHandedness.LeftHand);
    }

    protected override void OnSecondaryAttack()
    {
        AttackWithHand(WeaponHandedness.RightHand);
    }

    private void AttackWithHand(WeaponHandedness hand)
    {
        _attackHand = hand;
        base.PerformFire();
    }
}
