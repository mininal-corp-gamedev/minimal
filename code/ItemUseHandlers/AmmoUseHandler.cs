using System;
using System.Collections.Generic;
using Ambi.Storage;
using Sandbox;

namespace Megashot.ItemUseHandlers;

/// <summary>
/// Использование патронов: пополняет TotalReserveAmmo на 2*ClipSize у всех оружий семьи, если в инвентаре есть хотя бы одно оружие этой семьи (или подтип purple/gold).
/// </summary>
public sealed class AmmoUseHandler : IItemUseHandler
{
    private readonly string[] _weaponItemIds;
    private readonly int _clipSize;
    private readonly Func<IEnumerable<Weapon>> _getWeapons;

    public AmmoUseHandler(string[] weaponItemIds, int clipSize, Func<IEnumerable<Weapon>> getWeapons)
    {
        _weaponItemIds = weaponItemIds ?? Array.Empty<string>();
        _clipSize = clipSize;
        _getWeapons = getWeapons;
    }

    public bool Use(Item item, Player caller)
    {
        //if (caller?.Inventory == null)
        //    return false;

        //bool hasWeapon = false;
        //foreach (var weaponId in _weaponItemIds)
        //{
        //    if (caller.Inventory.GetTotalCount(weaponId) > 0)
        //    {
        //        hasWeapon = true;
        //        break;
        //    }
        //}

        //if (!hasWeapon)
        //    return false;

        var weapons = _getWeapons?.Invoke();
        if (weapons == null)
            return false;

        int toAdd = _clipSize * 2;
        foreach (var weapon in weapons)
        {
            if (weapon.IsValid())
                weapon.TotalReserveAmmo += toAdd;
        }

        item.Remove(1);
        return true;
    }
}
