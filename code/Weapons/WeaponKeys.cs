using Sandbox;

namespace Minimal.Weapons;
public sealed class WeaponKeys : Weapon
{
    [Property, Category("Combat")] public override bool IsMelee { get; set; } = true;
    [Property, Category("Reload")] public override bool HasReload { get; set; } = false;
    [Property, Category("Combat")] public override bool SemiAuto { get; set; } = true;
    [Property, Category("Combat")] public override float FireDelay { get; set; } = 0.45f;
    [Property, Category("Combat")] public override float Damage { get; set; } = 25f;
    [Property, Category("Combat")] public override float AttackRange { get; set; } = 80f;

    /// <summary>Sound played (broadcast at the door) when the door is successfully locked.</summary>
    [Property, Category("Sounds")] public SoundEvent LockSound { get; set; }

    /// <summary>Sound played (broadcast at the door) when the door is successfully unlocked.</summary>
    [Property, Category("Sounds")] public SoundEvent UnlockSound { get; set; }

    /// <summary>Sound played (broadcast at the door) when a non-owner/non-roommate just hits the door.</summary>
    [Property, Category("Sounds")] public SoundEvent DoorHitSound { get; set; }

    private bool _reloadWasReleased = true;

    protected override GameObject SpawnBullet(Vector3 origin, Vector3 direction)
    {
        return null;
    }

    protected override void PerformFire()
    {
        if (Player.Local?.IsArrested == true) return;

        var door = GetDoorInFront();
        if (door is null) return;

        if (door.CanBeControlledByLocalPlayer)
        {
            door.RpcRequestLockWithSound(LockSound);
        }
        else
        {
            // Not the owner / roommate / allowed-job: just knock on the door.
            door.RpcRequestHit(DoorHitSound);
        }
    }

    protected override void OnSecondaryAttack()
    {
        if (Player.Local?.IsArrested == true) return;

        var door = GetDoorInFront();
        if (door is null) return;

        if (door.CanBeControlledByLocalPlayer)
        {
            door.RpcRequestUnlockWithSound(UnlockSound);
        }
        else
        {
            door.RpcRequestHit(DoorHitSound);
        }
    }

    protected override void OnWeaponFixedUpdate()
    {
        if (Player.Local?.IsArrested == true) return;

        if (!_reloadWasReleased && !Input.Down("Reload"))
            _reloadWasReleased = true;

        if (Input.Pressed("Reload") && _reloadWasReleased)
        {
            _reloadWasReleased = false;
            TryOpenDoorMenu();
        }
    }

    private Door GetDoorInFront()
    {
        if (Player.Local?.Controller is null) return null;

        var eyePos = Player.Local.Controller.EyePosition;
        var eyeDir = Player.Local.Controller.EyeTransform.Forward;
        var range = 200f;

        var tr = Scene.Trace
            .Ray(eyePos, eyePos + eyeDir * range)
            .IgnoreGameObjectHierarchy(Player.Local.GameObject)
            .Run();

        if (!tr.Hit) return null;

        // Find Door component
        Door targetDoor = null;
        var go = tr.GameObject;
        while (go.IsValid())
        {
            if (go.Components.TryGet<Door>(out var door, FindMode.EverythingInSelfAndParent))
            {
                targetDoor = door;
                break;
            }
            go = go.Parent;
        }

        return targetDoor;
    }

    private void TryOpenDoorMenu()
    {
        var door = GetDoorInFront();
        if (door is null) return;

        // Find DoorHud in scene and open it
        var doorHud = Scene.GetAllComponents<DoorHud>().FirstOrDefault();
        if (doorHud is null) return;

        if (!doorHud.CanOpen) return;

        doorHud.Door = door;
    }
}
