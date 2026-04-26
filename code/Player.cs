using Ambi.Storage;
using Megashot.ItemUseHandlers;
using Sandbox;
using System;

public sealed class Player : Component, Component.IDamageable
{
    public static Player Local { get; private set; }

    [Property] public PlayerController Controller { get; private set; }
    [Property] public SkinnedModelRenderer Renderer { get; private set; }
    [Property] public Dresser Dresser { get; private set; }
    [Property] public PlayerWorldHud WorldHud { get; private set; }
    [Property, Sync(SyncFlags.FromHost)] public PlayerJob Job { get; private set; }
    [Property] public GameObject ItemDropPrefab { get; private set; }
    [Property, Category("Sounds")] public SoundEvent HitSound { get; set; }

    [Sync(SyncFlags.FromHost)] public float Health { get; set; } = 100f;
    [Sync(SyncFlags.FromHost)] public float MaxHealth { get; set; } = 100f;
    [Sync(SyncFlags.FromHost)] public int Money { get; set; } = 0;
    public int CactusCount { get; set; } = 0;
    public bool IsAlive => Health > 0;
    public Inventory Inventory { get; set; } = new(10);

    public Weapon CurrentWeapon { get; private set; }
    public int CurrentInventorySlotIndex { get; private set; } = -1;
    public string CurrentWeaponItemId { get; private set; }

    public bool IsLocalPlayer => !IsProxy;

    private static bool _itemUseHandlersRegistered;

    public void Spawn()
    {
        if (IsProxy) return;

        var spawnPoint = SpawnManager.Instance?.GetRandomPlayerSpawn();
        if (!spawnPoint.IsValid()) return;

        Health = MaxHealth;
        WorldHud?.WorldHudRefresh();
        WorldPosition = spawnPoint.WorldPosition;
        Controller.EyeAngles = spawnPoint.WorldRotation;

        Job?.NotifySpawned();
    }

    public void OnDamage(in DamageInfo dmgInfo)
    {
        // Локальные источники урона (окружение и т.п.) — только на авторитетной копии.
        if (IsProxy) return;

        TakeDamageFromWeapon(dmgInfo.Damage, dmgInfo.Attacker);
    }

    /// <summary>
    /// Урон от оружия другого игрока. <c>[Rpc.Owner]</c> доставляет вызов на машину владельца этого Player,
    /// где <c>[Sync] Health</c> можно записать и изменение синхронизируется всем.
    /// </summary>
    public void TakeDamageFromWeapon(float damage, GameObject attacker = null)
    {
        RpcTakeDamageFromWeapon(damage, attacker);
    }

    [Rpc.Broadcast]
    public void RpcOnWeaponFired(SoundEvent fireSound, Vector3 soundPos, GameObject muzzlePrefab, Vector3 muzzlePos, Rotation muzzleRot, GameObject bullet)
    {
        Renderer?.Set("b_attack", true);
        if (fireSound.IsValid())
            Sound.Play(fireSound, soundPos);
        if (muzzlePrefab.IsValid())
        {
            var obj = muzzlePrefab.Clone(muzzlePos, muzzleRot);
            var destroy = obj.Components.Create<DestroyAfterSeconds>();
            destroy.Seconds = 0.5f;
        }

        if (bullet.IsValid())
            bullet.NetworkSpawn();
    }

    [Rpc.Broadcast]
    private void RpcOnPlayerHit(SkinnedModelRenderer renderer)
    {
        renderer.Set("hit", true);
    }

    [Rpc.Broadcast]
    private void RpcSetHoldType(SkinnedModelRenderer renderer, int holdType)
    {
        renderer?.Set("holdtype", holdType);
    }

    [Rpc.Owner]
    private void RpcTakeDamageFromWeapon(float damage, GameObject attacker)
    {
        if (attacker == Local.GameObject) return;

        if (damage <= 0f) return;

        Health = Math.Max(0f, Health - damage);
        WorldHud?.WorldHudRefresh();

        RpcOnPlayerHit(Renderer);

        if (Health <= 0f)
            Die();
    }

    public void Die()
    {
        if (IsProxy) return;

        Spawn();
    }

    public void SwitchWeapon(Weapon wep = null)
    {
        if (IsProxy) return;

        if (CurrentWeapon.IsValid() && wep == CurrentWeapon) return;

        CurrentWeapon?.GameObject.Enabled = false;

        if (!wep.IsValid())
        {
            CurrentWeapon = null;
            CurrentInventorySlotIndex = -1;
            CurrentWeaponItemId = null;
            RpcSetHoldType(Renderer, 0);

            return;
        }

        CurrentWeapon = wep;
        CurrentWeapon.GameObject.Enabled = true;
        RpcSetHoldType(Renderer, (int)CurrentWeapon.HoldType);
    }

    public bool UseInventorySlot(int slotIndex)
    {
        if (IsProxy) return false;
        if (Inventory == null) return false;
        if (slotIndex < 0 || slotIndex >= Inventory.Slots.Count) return false;

        var slot = Inventory.Slots[slotIndex];
        if (slot.IsEmpty || slot.Item == null)
        {
            if (CurrentWeapon.IsValid())
            {
                SwitchWeapon();
                return true;
            }

            return false;
        }

        if (!slot.Item.Definition.CanUse) return false;

        var itemId = slot.Item.Id;
        var isWeapon = IsWeaponItem(slot.Item);
        var successful = Inventory.TryUseItem(slot, this);

        if (successful && isWeapon)
        {
            CurrentInventorySlotIndex = slotIndex;
            CurrentWeaponItemId = itemId;
        }

        ValidateCurrentWeaponInventoryState();

        return successful;
    }

    private static bool IsWeaponItem(Item item)
    {
        var category = item?.Definition?.Category;
        return string.Equals(category, "weapon", StringComparison.OrdinalIgnoreCase)
            || string.Equals(category, "weapons", StringComparison.OrdinalIgnoreCase);
    }

    private void CheckUseHotbarSlots()
    {
        if (IsProxy) return;

        if (Input.Pressed("Slot1"))
            UseInventorySlot(0);
        else if (Input.Pressed("Slot2"))
            UseInventorySlot(1);
        else if (Input.Pressed("Slot3"))
            UseInventorySlot(2);
        else if (Input.Pressed("Slot4"))
            UseInventorySlot(3);
        else if (Input.Pressed("Slot5"))
            UseInventorySlot(4);
        else if (Input.Pressed("Slot6"))
            UseInventorySlot(5);
    }

    private void ValidateCurrentWeaponInventoryState()
    {
        if (CurrentWeaponItemId == null)
            return;

        var matchingSlotIndex = FindInventorySlotIndex(CurrentWeaponItemId);
        if (matchingSlotIndex >= 0)
        {
            CurrentInventorySlotIndex = matchingSlotIndex;
            return;
        }

        CurrentInventorySlotIndex = -1;
        CurrentWeaponItemId = null;
        SwitchWeapon();
    }

    private int FindInventorySlotIndex(string itemId)
    {
        if (Inventory == null)
            return -1;

        for (int i = 0; i < Inventory.Slots.Count; i++)
        {
            var slot = Inventory.Slots[i];
            if (!slot.IsEmpty && slot.Item.Id == itemId)
                return i;
        }

        return -1;
    }

    public void DropItem(Slot slot, int count = 1)
    {
        if (count <= 0) return;
        if (slot.IsEmpty) return;
        if (slot.Item.Count < count) return;

        var item = slot.Item;

        var pos = WorldPosition + Controller.EyeTransform.Forward * 90f + Controller.EyeTransform.Up * 80f; // Slightly above so it doesn't get stuck in ground
        var gameObj = ItemDropPrefab.Clone(pos);
        var itemComponent = gameObj.GetComponent<ItemComponent>();

        itemComponent.Count = count;
        itemComponent.ItemDefinition = item.Definition;
        itemComponent.UsePickupMagnet = false;
        itemComponent.DelayDestroy = 10f;
        itemComponent.DelayDeleteSpawn = true;

        if (item.Definition.Model.IsValid())
        {
            itemComponent.Model.Model = item.Definition.Model;
            gameObj.GetComponent<ModelCollider>(true).Model = item.Definition.Model;
        }

        Inventory.RemoveItem(slot, count);
        ValidateCurrentWeaponInventoryState();

        Notification.Make($"Relic dropped: {item.Definition.Header}", 10f);
    }

    [Rpc.Host]
    private void DressForHost(Dresser dresser)
    {
        Log.Info($"Dresser from: {Rpc.Caller.DisplayName} - {dresser.Network.Owner.DisplayName}");

        Dresser.Clear();
        Dresser.Apply();
    }

    private void SetupWorldHud()
    {
        WorldHud.Name = Connection.Local.DisplayName;
    }

    private void GiveStartingItems()
    {
        Inventory.AddItem( Item.Create( "usp", 1 ) ); // TODO: Remove starter USP after testing inventory weapon flow.
        Inventory.AddItem( Item.Create( "ammo_usp", 15 ) );
        Inventory.AddItem( Item.Create( "ammo_mp5", 20 ) );
        Inventory.AddItem( Item.Create( "ammo_m4a1", 10 ) );
    }

    private static void RegisterItemUseHandlers()
    {
        if (_itemUseHandlersRegistered)
            return;

        ItemUseRegistry.Register("usp", new WepUspUseHandler());
        ItemUseRegistry.Register("mp5", new WepMp5UseHandler());
        ItemUseRegistry.Register("m4a1", new WepM4a1UseHandler());
        ItemUseRegistry.Register("ammo_usp", new AmmoUseHandler(new[] { "usp" }, 12, () => new[] { WeaponManager.Instance?.Usp }));
        ItemUseRegistry.Register("ammo_mp5", new AmmoUseHandler(new[] { "mp5" }, 30, () => new[] { WeaponManager.Instance?.Mp5 }));
        ItemUseRegistry.Register("ammo_m4a1", new AmmoUseHandler(new[] { "m4a1" }, 30, () => new[] { WeaponManager.Instance?.M4A1 }));

        _itemUseHandlersRegistered = true;
    }

    private void NetworkInit()
    {
        if (IsProxy) return;

        RegisterItemUseHandlers();
        Inventory.OnChanged += ValidateCurrentWeaponInventoryState;
        Job?.AssignDefault();
        Spawn();
        SetupWorldHud();
        DressForHost(Dresser);
        GiveStartingItems();
    }

    private void MakeLocalInstance()
    {
        if (!IsProxy)
            Local = this;
    }

    private void DestroyLocalInstance()
    {
        if (Local == this)
            Local = null;
    }

    protected override void OnStart()
	{
        MakeLocalInstance();
        NetworkInit();
    }

    protected override void OnFixedUpdate()
    {
        CheckUseHotbarSlots();
    }

    protected override void OnDestroy()
    {
        if (Inventory != null)
            Inventory.OnChanged -= ValidateCurrentWeaponInventoryState;

        DestroyLocalInstance();
    }


    public void TakeBox( int amount )
    {
        Money += amount;
    
        RpcNotifyTakeBox( amount );
    }
 
    [Rpc.Owner]
    private void RpcNotifyTakeBox( int amount )
    {
        Notification.Info( $"Ты лутанул ${amount}", 3.5f );
    }

    [Rpc.Owner]
    public void RpcColorSlotResult( bool won, bool resultGreen, bool noMoney )
    {
        ColorSlotState.Instance?.OnResult( won, resultGreen, noMoney );
    }
}
