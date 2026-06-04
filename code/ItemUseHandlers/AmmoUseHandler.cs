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
#if SERVER
        if ( Networking.IsHost )
        {
            if ( !caller.HostTryUseAmmo( item, _weaponType ) )
                return false;

            // On a listen host the local weapon instance may exist on the host too.
            var weapon = GetWeapon();
            if ( weapon.IsValid() )
                weapon.TotalReserveAmmo += weapon.ClipSize * 2;

            return true;
        }
#else
        var weapon = GetWeapon();
        if ( !weapon.IsValid() )
            return false;

        weapon.TotalReserveAmmo += weapon.ClipSize * 2;
        return true;
#endif

        return false;
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
    Shotgun
}
