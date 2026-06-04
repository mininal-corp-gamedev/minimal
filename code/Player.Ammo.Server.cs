using Ambi.Storage;
using Minimal.ItemUseHandlers;
using Sandbox;

public sealed partial class Player
{
    public bool HostTryUseAmmo( Item item, AmmoWeaponType weaponType )
    {
        if ( !Networking.IsHost )
            return false;
        if ( item is null )
            return false;

        var weaponItemId = AmmoWeaponTypeToWeaponItemId( weaponType );
        if ( string.IsNullOrWhiteSpace( weaponItemId ) )
            return false;

        if ( FindInventorySlotIndex( weaponItemId ) < 0 )
            return false;

        item.Remove( 1 );
        return true;
    }

    private static string AmmoWeaponTypeToWeaponItemId( AmmoWeaponType weaponType )
    {
        return weaponType switch
        {
            AmmoWeaponType.Usp => "usp",
            AmmoWeaponType.Mp5 => "mp5",
            AmmoWeaponType.M4A1 => "m4a1",
            AmmoWeaponType.Shotgun => "shotgun",
            _ => null
        };
    }
}
