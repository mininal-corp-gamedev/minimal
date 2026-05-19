using Ambi.Storage;
using Sandbox;

namespace Minimal.ItemUseHandlers;

/// <summary>
/// Использование патронов: пополняет TotalReserveAmmo у оружия этой семьи.
/// </summary>
public sealed class AmmoUseHandler : IItemUseHandler
{
    private readonly AmmoWeaponType _weaponType;

    public AmmoUseHandler(AmmoWeaponType weaponType)
    {
        _weaponType = weaponType;
    }

    public bool Use(Item item, Player caller)
    {
        var weapon = GetWeapon();
        if (weapon.IsValid())
            weapon.TotalReserveAmmo += weapon.ClipSize * 2;
        else if (!Networking.IsHost)
            return false;

        item.Remove(1);
        return true;
    }

    private Weapon GetWeapon()
    {
        var manager = WeaponManager.Instance;
        if (!manager.IsValid())
            return null;

        return _weaponType switch
        {
            AmmoWeaponType.Usp => manager.Usp,
            AmmoWeaponType.Mp5 => manager.Mp5,
            AmmoWeaponType.M4A1 => manager.M4A1,
            AmmoWeaponType.Revolver => manager.Revolver,
            AmmoWeaponType.Shotgun => manager.Shotgun,
            _ => null
        };
    }
}

public enum AmmoWeaponType
{
    Usp,
    Mp5,
    M4A1,
    Revolver,
    Shotgun
}
