using Sandbox;

/// <summary>
/// Единственный менеджер оружия на сцене. Хранит префабы и экземпляры всех типов оружия.
/// Оружие спавнится как дочерние объекты камеры игрока и переключается через SwitchWeapon.
/// </summary>
public sealed class WeaponManager : Component
{
    public static WeaponManager Instance { get; private set; }

    [Property] public GameObject BulletPrefab { get; set; }

    [Property, Category("Weapon Prefabs")] public GameObject WeaponPickaxe { get; set; }
    [Property, Category("Weapon Prefabs")] public GameObject WeaponMp5Prefab { get; set; }
    [Property, Category("Weapon Prefabs")] public GameObject WeaponM4a1Prefab { get; set; }
    [Property, Category("Weapon Prefabs")] public GameObject WeaponUspPrefab { get; set; }

    /// <summary>Слот ближнего боя (без патронов). Префаб — <see cref="Megashot.Weapons.WeaponPickaxe"/> или <c>Weapon</c> с <c>IsMelee</c>.</summary>
    public Weapon Pickaxe { get; private set; }
    public Weapon Mp5 { get; private set; }
    public Weapon M4A1 { get; private set; }
    public Weapon Usp { get; private set; }

    /// <summary>Подходит ли оружие под отображение патронов / перезарядку.</summary>
    public static bool WeaponUsesAmmo(Weapon weapon) => weapon.IsValid() && !weapon.IsMelee;

    protected override void OnAwake()
    {
        if (Instance == null)
            Instance = this;
    }

    protected override void OnDestroy()
    {
        if (Instance != null)
            Instance = null;
    }

    protected override void OnStart()
    {
        SpawnAllWeapons();
    }

    private void SpawnAllWeapons()
    {
        var camera = Scene.Camera?.GameObject;
        if (!camera.IsValid())
        {
            Log.Warning("[WeaponManager] No scene camera, weapons will not be parented");
            return;
        }

        Mp5 = SpawnWeapon(WeaponMp5Prefab, camera, "MP5");
        M4A1 = SpawnWeapon(WeaponM4a1Prefab, camera, "M4A1");
        Usp = SpawnWeapon(WeaponUspPrefab, camera, "USP");
        Pickaxe = SpawnWeapon(WeaponPickaxe, camera, "Pickaxe");

        Log.Info("[WeaponManager] Spawned all weapons");
    }

    private Weapon SpawnWeapon(GameObject prefab, GameObject parent, string name)
    {
        Log.Info("[WeaponManager] Spawning weapon: " + name);

        if (!prefab.IsValid())
        {
            Log.Warning($"[WeaponManager] Prefab for {name} not set, skipping");
            return null;
        }

        var obj = prefab.Clone();
        obj.Name = name;
        obj.Enabled = false;
        obj.Parent = parent;
        obj.LocalPosition = Vector3.Zero;
        obj.LocalRotation = Rotation.Identity;

        if (obj.Components.TryGet<Weapon>(out var weapon, FindMode.EverythingInSelfAndParent))
        {
            Log.Info($"[WeaponManager] Spawned {name}");
            return weapon;
        }

        Log.Warning($"[WeaponManager] Prefab {name} has no Weapon component");
        return null;
    }
}
