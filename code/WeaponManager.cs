using Sandbox;

/// <summary>
/// Единственный менеджер оружия на сцене. Хранит префабы и экземпляры всех типов оружия.
/// Оружие спавнится как дочерние объекты камеры игрока и переключается через SwitchWeapon.
/// </summary>
public sealed class WeaponManager : Component
{
    public static WeaponManager Instance { get; private set; }

    [Property] public GameObject BulletPrefab { get; set; }
    [Property] public Weapon Wep { get; set; }

    [Property, Category("Weapon Prefabs")] public GameObject WeaponPickaxe { get; set; }
    [Property, Category("Weapon Prefabs")] public GameObject WeaponUspPrefab { get; set; }
    [Property, Category("Weapon Prefabs")] public GameObject WeaponMp5Prefab { get; set; }
    [Property, Category("Weapon Prefabs")] public GameObject WeaponM4a1Prefab { get; set; }
    [Property, Category("Weapon Prefabs")] public GameObject WeaponShotgunPrefab { get; set; }
    [Property, Category("Weapon Prefabs")] public GameObject WeaponPhysgun { get; set; }
    [Property, Category("Weapon Prefabs")] public GameObject WeaponToolgun { get; set; }
    [Property, Category("Weapon Prefabs")] public GameObject WeaponHands { get; set; }
    [Property, Category("Weapon Prefabs")] public GameObject WeaponHandcuff { get; set; }
    [Property, Category("Weapon Prefabs")] public GameObject WeaponPicklock { get; set; }
    [Property, Category("Weapon Prefabs")] public GameObject WeaponKeys { get; set; }
    [Property, Category("Weapon Prefabs")] public GameObject WeaponBurger { get; set; }

    [Property, Category("World Weapon Prefabs")] public GameObject WorldWeaponPickaxe { get; set; }
    [Property, Category("World Weapon Prefabs")] public GameObject WorldWeaponUspPrefab { get; set; }
    [Property, Category("World Weapon Prefabs")] public GameObject WorldWeaponMp5Prefab { get; set; }
    [Property, Category("World Weapon Prefabs")] public GameObject WorldWeaponM4a1Prefab { get; set; }
    [Property, Category("World Weapon Prefabs")] public GameObject WorldWeaponShotgunPrefab { get; set; }
    [Property, Category("World Weapon Prefabs")] public GameObject WorldWeaponPhysgun { get; set; }
    [Property, Category("World Weapon Prefabs")] public GameObject WorldWeaponToolgun { get; set; }
    [Property, Category("World Weapon Prefabs")] public GameObject WorldWeaponHandcuff { get; set; }
    [Property, Category("World Weapon Prefabs")] public GameObject WorldWeaponPicklock { get; set; }
    [Property, Category("World Weapon Prefabs")] public GameObject WorldWeaponKeys { get; set; }

    /// <summary>Слот ближнего боя (без патронов). Префаб — <see cref="Minimal.Weapons.WeaponPickaxe"/> или <c>Weapon</c> с <c>IsMelee</c>.</summary>
    public Weapon Pickaxe { get; private set; }
    public Weapon Mp5 { get; private set; }
    public Weapon M4A1 { get; private set; }
    public Weapon Shotgun { get; private set; }
    public Weapon Usp { get; private set; }
    public Weapon Physgun { get; private set; }
    public Weapon Toolgun { get; private set; }
    public Weapon Hands { get; private set; }
    public Weapon Handcuff { get; private set; }
    public Weapon Picklock { get; private set; }
    public Weapon Keys { get; private set; }
    public Weapon Burger { get; private set; }
    private bool _weaponsSpawned;

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
        TrySpawnAllWeapons(logIfMissingCamera: true);
    }

    protected override void OnUpdate()
    {
        if (_weaponsSpawned) return;

        TrySpawnAllWeapons(logIfMissingCamera: false);
    }

    private void TrySpawnAllWeapons(bool logIfMissingCamera)
    {
        var camera = Scene.Camera?.GameObject;
        if (!camera.IsValid())
        {
            if (logIfMissingCamera)
                Log.Warning("[WeaponManager] No scene camera, weapons will not be parented");
            return;
        }

        Mp5 = SpawnWeapon(WeaponMp5Prefab, camera, "MP5");
        M4A1 = SpawnWeapon(WeaponM4a1Prefab, camera, "M4A1");
        Shotgun = SpawnWeapon(WeaponShotgunPrefab, camera, "Shotgun");
        Usp = SpawnWeapon(WeaponUspPrefab, camera, "USP");
        Pickaxe = SpawnWeapon(WeaponPickaxe, camera, "Pickaxe");
        Physgun = SpawnWeapon(WeaponPhysgun, camera, "Physgun");
        Toolgun = SpawnWeapon(WeaponToolgun, camera, "Toolgun");
        Hands = SpawnWeapon(WeaponHands, camera, "Hands");
        Handcuff = SpawnWeapon(WeaponHandcuff, camera, "Handcuff");
        Picklock = SpawnWeapon(WeaponPicklock, camera, "Picklock");
        Keys = SpawnWeapon(WeaponKeys, camera, "Keys");
        Burger = SpawnWeapon(WeaponBurger, camera, "Burger");
        _weaponsSpawned = true;

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
