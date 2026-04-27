using Ambi.Storage;
using Ambi.Utils;
using Minimal.ItemUseHandlers;
using Sandbox;
using System;
using System.Text.Json.Serialization;

public sealed class Player : Component, ICustomDamagable
{
    public static Player Local { get; private set; }

    [Property] public PlayerController Controller { get; private set; }
    [Property] public SkinnedModelRenderer Renderer { get; private set; }
    [Property] public Dresser Dresser { get; private set; }
    [Property] public PlayerWorldHud WorldHud { get; private set; }
    [Property, Sync(SyncFlags.FromHost)] public PlayerJob Job { get; private set; }
    [Property] public GameObject ItemDropPrefab { get; private set; }
    [Property] public GameObject MoneyDropPrefab { get; private set; }
    [Property, Category("Sounds")] public SoundEvent HitSound { get; set; }

    /// <summary>Maximum number of doors this player can own at once.</summary>
    [Property] public int MaxDoors { get; set; } = 8;

    [Sync(SyncFlags.FromHost)] public float Health { get; set; } = 100f;
    [Sync(SyncFlags.FromHost)] public float MaxHealth { get; set; } = 100f;

    private int _money;
    [Sync(SyncFlags.FromHost)]
    public int Money
    {
        get => _money;
        set
        {
            if ( _money == value ) return;
            _money = value;
            if ( Networking.IsHost && _saveInitialized )
                SavePlayerData();
        }
    }

    [Sync(SyncFlags.FromHost)] public int AdminRank { get; set; } = 0;

    // ===== Arrest system =====
    /// <summary>Точка спавна арестованного игрока. Выставляется в редакторе.</summary>
    [Property, Category("Arrest")] public GameObject ArrestSpawnPoint { get; set; }

    /// <summary>Длительность ареста в секундах.</summary>
    [Property, Category("Arrest")] public float ArrestDurationSeconds { get; set; } = 120f;

    /// <summary>Радиус взаимодействия наручников (используется хостом для валидации).</summary>
    [Property, Category("Arrest")] public float ArrestInteractRange { get; set; } = 110f;

    /// <summary>Арестован ли игрок. Меняется только хостом.</summary>
    [Sync(SyncFlags.FromHost)] public bool IsArrested { get; set; }

    /// <summary>Время до автоматического освобождения. Считается на клиенте, по истечении клиент шлёт RPC хосту.</summary>
    [Sync(SyncFlags.FromHost)] public TimeUntil ArrestTimeUntilRelease { get; set; }

    private bool _arrestSpeedApplied;
    private float _origWalkSpeed;
    private float _origRunSpeed;

    // ===== Lockpick cooldown =====
    /// <summary>Время до окончания кулдауна на взлом дверей. Хост авторитетен.</summary>
    [Sync(SyncFlags.FromHost)] public TimeUntil LockpickCooldown { get; set; }

    private const string PlayerSaveFolder = "players";
    private const int DefaultStartingMoney = 500;

    // Host-only gate. Until the save is loaded on the host, Money writes
    // must not overwrite the file on disk.
    private bool _saveInitialized;

    /// <summary>
    /// Number of doors currently owned (gameplay-wise) by this player.
    /// A paired (double) door counts as a single entry.
    /// </summary>
    public int OwnedDoorsCount
    {
        get
        {
            var visited = new HashSet<Door>();
            int count = 0;

            foreach (var go in Scene.GetAllObjects(true))
            {
                if (!go.Components.TryGet<Door>(out var door)) continue;
                if (door.PlayerOwner != this) continue;
                if (!visited.Add(door)) continue;

                if (door.DoorSecond.IsValid())
                    visited.Add(door.DoorSecond);

                count++;
            }

            return count;
        }
    }
    public int CactusCount { get; set; } = 0;
    public bool IsAlive => Health > 0;
    public Inventory Inventory { get; set; } = new(20);

    public Weapon CurrentWeapon { get; private set; }
    public int CurrentInventorySlotIndex { get; private set; } = -1;
    public int SelectedHotbarSlotIndex { get; private set; } = -1;
    public string CurrentWeaponItemId { get; private set; }

    public bool IsLocalPlayer => !IsProxy;

    private static bool _itemUseHandlersRegistered;

    public sealed class PlayerSaveData
    {
        [JsonPropertyName( "steamId" )] public long SteamId { get; set; }
        [JsonPropertyName( "money" )] public int Money { get; set; }
    }

    public void Spawn()
    {
        if (IsProxy) return;

        SpawnInternal();
    }

    public void AdminRespawn()
    {
        if (!Networking.IsHost) return;

        SpawnInternal();
    }

    private void SpawnInternal()
    {
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
        if (IsArrested) return;

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
        //if (!renderer.IsValid()) return;

        renderer.Set("hit", true);
    }

    [Rpc.Broadcast]
    private void RpcSetHoldType(SkinnedModelRenderer renderer, int holdType)
    {
        //if (!renderer.IsValid()) return;

        renderer?.Set("holdtype", holdType);
    }

    [Rpc.Owner]
    private void RpcTakeDamageFromWeapon(float damage, GameObject attacker)
    {
        if (attacker == Local.GameObject) return;
        if (IsArrested) return;

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
        if (IsArrested && wep.IsValid()) return; // Арестованный не может взять оружие в руки

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
        if (IsArrested) return false; // Арестованный не может пользоваться предметами
        if (Inventory == null) return false;
        if (slotIndex < 0 || slotIndex >= Inventory.Slots.Count) return false;

        if (slotIndex >= 0 && slotIndex < 9)
            SelectedHotbarSlotIndex = slotIndex;

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
        if (IsArrested) return; // Арестованный не может переключать слоты/оружие

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
        else if (Input.Pressed("Slot7"))
            UseInventorySlot(6);
        else if (Input.Pressed("Slot8"))
            UseInventorySlot(7);
        else if (Input.Pressed("Slot9"))
            UseInventorySlot(8);
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
        if (!ItemDropPrefab.IsValid()) return;

        var item = slot.Item;

        var pos = WorldPosition + Controller.EyeTransform.Forward * 90f + Controller.EyeTransform.Up * 80f; // Slightly above so it doesn't get stuck in ground
        var gameObj = ItemDropPrefab.Clone(pos);
        var itemComponent = gameObj.GetComponent<ItemComponent>();
        if (!itemComponent.IsValid())
        {
            gameObj.Destroy();
            return;
        }

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

        gameObj.NetworkSpawn(GameObject.Network.Owner);

        Inventory.RemoveItem(slot, count);
        ValidateCurrentWeaponInventoryState();

        Notification.Make($"You dropped: {item.Definition.Header}", 10f);
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
        Inventory.AddItem( Item.Create( "handcuff", 1 ) ); // TODO: Remove starter USP after testing inventory weapon flow.
        Inventory.AddItem(Item.Create("picklock", 1)); // TODO: Remove starter USP after testing inventory weapon flow.
        Inventory.AddItem(Item.Create("usp", 1)); // TODO: Remove starter USP after testing inventory weapon flow.
    }

    private static void RegisterItemUseHandlers()
    {
        if (_itemUseHandlersRegistered)
            return;

        ItemUseRegistry.Register("usp", new WepUspUseHandler());
        ItemUseRegistry.Register("mp5", new WepMp5UseHandler());
        ItemUseRegistry.Register("m4a1", new WepM4a1UseHandler());
        ItemUseRegistry.Register("physgun", new WepPhysgunUseHandler());
        ItemUseRegistry.Register("toolgun", new WepToolgunUseHandler());
        ItemUseRegistry.Register("hands", new WepHandsUseHandler());
        ItemUseRegistry.Register("handcuff", new WepHandcuffUseHandler());
        ItemUseRegistry.Register("picklock", new WepPicklockUseHandler());
        ItemUseRegistry.Register("ammo_usp", new AmmoUseHandler(AmmoWeaponType.Usp));
        ItemUseRegistry.Register("ammo_mp5", new AmmoUseHandler(AmmoWeaponType.Mp5));
        ItemUseRegistry.Register("ammo_m4a1", new AmmoUseHandler(AmmoWeaponType.M4A1));

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
        AdminManager.RpcRequestRankInit();
    }

    private long GetOwnerSteamId()
    {
        return GameObject.Network.Owner?.SteamId.Value ?? 0L;
    }

    private static string GetPlayerSavePath( long steamId ) => $"{PlayerSaveFolder}/{steamId}.json";

    private static void EnsurePlayerSaveFolder()
    {
        FileSystem.Data.CreateDirectory( PlayerSaveFolder );
    }

    /// <summary>
    /// Host-only. Loads save for the owning SteamId, applies it to this Player,
    /// and unlocks further persistence. If no save exists, issues the starting money.
    /// </summary>
    private void HostInitSave()
    {
        if ( !Networking.IsHost ) return;

        var steamId = GetOwnerSteamId();
        if ( steamId == 0L )
        {
            Log.Warning( "[PlayerSave] Cannot init save: owner SteamId is 0." );
            return;
        }

        EnsurePlayerSaveFolder();

        PlayerSaveData data = null;
        try
        {
            var path = GetPlayerSavePath( steamId );
            if ( FileSystem.Data.FileExists( path ) )
                data = FileSystem.Data.ReadJsonOrDefault<PlayerSaveData>( path );
        }
        catch ( Exception ex )
        {
            Log.Warning( $"[PlayerSave] Load failed for {steamId}: {ex.Message}" );
        }

        if ( data is null )
        {
            data = new PlayerSaveData
            {
                SteamId = steamId,
                Money = DefaultStartingMoney
            };
        }

        // Apply to this player (bypasses persistence because _saveInitialized is still false).
        _money = data.Money;
        _saveInitialized = true;

        // Persist a fresh copy so new players get a file immediately.
        SavePlayerData();
    }

    /// <summary>
    /// Host-only. Writes the current player state to disk. Guarded by
    /// <see cref="_saveInitialized"/> so the save can never be clobbered
    /// before it has been loaded.
    /// </summary>
    private void SavePlayerData()
    {
        if ( !Networking.IsHost ) return;
        if ( !_saveInitialized ) return;

        var steamId = GetOwnerSteamId();
        if ( steamId == 0L ) return;

        try
        {
            EnsurePlayerSaveFolder();
            var data = new PlayerSaveData
            {
                SteamId = steamId,
                Money = _money
            };
            FileSystem.Data.WriteJson( GetPlayerSavePath( steamId ), data );
        }
        catch ( Exception ex )
        {
            Log.Warning( $"[PlayerSave] Save failed for {steamId}: {ex.Message}" );
        }
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
        HostInitSave();
        NetworkInit();
    }

    protected override void OnFixedUpdate()
    {
        UpdateArrestEffects();
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

    public void RequestDropMoney( int amount )
    {
        if ( amount <= 0 ) return;
        RpcRequestDropMoney( amount );
    }

    public void RequestTransferMoney( long targetSteamId, int amount )
    {
        if ( targetSteamId <= 0 || amount <= 0 ) return;
        RpcRequestTransferMoney( targetSteamId, amount );
    }

    [Rpc.Host]
    private void RpcRequestDropMoney( int amount )
    {
        if ( !Networking.IsHost ) return;

        var caller = Rpc.Caller;
        if ( caller is null ) return;

        var player = FindPlayerBySteamId( caller.SteamId.Value );
        if ( !player.IsValid() )
        {
            NotifyMoneyResult( caller, "Твой игрок ещё не готов.", false );
            return;
        }

        if ( amount <= 0 )
        {
            NotifyMoneyResult( caller, "Некорректная сумма.", false );
            return;
        }

        if ( player.Money < amount )
        {
            NotifyMoneyResult( caller, "Недостаточно денег.", false );
            return;
        }

        if ( !player.MoneyDropPrefab.IsValid() )
        {
            NotifyMoneyResult( caller, "Префаб денег не настроен.", false );
            return;
        }

        if ( !TrySpawnDroppedMoney( player, caller, amount ) )
        {
            NotifyMoneyResult( caller, "Не удалось выкинуть деньги.", false );
            return;
        }

        player.Money -= amount;
        NotifyMoneyResult( caller, $"Ты выкинул ${amount}.", true );
    }

    [Rpc.Host]
    private void RpcRequestTransferMoney( long targetSteamId, int amount )
    {
        if ( !Networking.IsHost ) return;

        var caller = Rpc.Caller;
        if ( caller is null ) return;

        var player = FindPlayerBySteamId( caller.SteamId.Value );
        if ( !player.IsValid() )
        {
            NotifyMoneyResult( caller, "Твой игрок ещё не готов.", false );
            return;
        }

        if ( amount <= 0 )
        {
            NotifyMoneyResult( caller, "Некорректная сумма.", false );
            return;
        }

        if ( player.Money < amount )
        {
            NotifyMoneyResult( caller, "Недостаточно денег.", false );
            return;
        }

        if ( targetSteamId == caller.SteamId.Value )
        {
            NotifyMoneyResult( caller, "Нельзя передать деньги себе.", false );
            return;
        }

        var target = FindPlayerBySteamId( targetSteamId );
        if ( !target.IsValid() )
        {
            NotifyMoneyResult( caller, "Игрок для передачи не найден.", false );
            return;
        }

        if ( !CanTransferToTarget( player, target ) )
        {
            NotifyMoneyResult( caller, "Игрок слишком далеко или не перед тобой.", false );
            return;
        }

        player.Money -= amount;
        target.Money += amount;

        var targetConnection = target.GameObject.Network.Owner;
        NotifyMoneyResult( caller, $"Ты передал ${amount} игроку {GetConnectionName( targetConnection )}.", true );

        if ( targetConnection is not null )
            NotifyMoneyResult( targetConnection, $"{caller.DisplayName} передал тебе ${amount}.", true );
    }

    private static bool TrySpawnDroppedMoney( Player player, Connection owner, int amount )
    {
        var forward = player.Controller.IsValid()
            ? player.Controller.EyeTransform.Forward
            : player.WorldRotation.Forward;

        var yawForward = new Vector3( forward.x, forward.y, 0f );
        if ( yawForward.LengthSquared <= 0.001f )
            yawForward = player.WorldRotation.Forward;

        yawForward = yawForward.Normal;

        var spawnPosition = player.WorldPosition + yawForward * 70f + Vector3.Up * 28f;
        var moneyObject = player.MoneyDropPrefab.Clone( spawnPosition, Rotation.LookAt( yawForward ) );
        if ( !moneyObject.IsValid() )
            return false;

        var money = moneyObject.Components.Get<MoneyDropped>();
        if ( money.IsValid() )
            money.Money = amount;

        moneyObject.NetworkSpawn( owner );
        return true;
    }

    private static bool CanTransferToTarget( Player player, Player target )
    {
        if ( !player.IsValid() || !target.IsValid() )
            return false;

        if ( Vector3.DistanceBetween( player.WorldPosition, target.WorldPosition ) > 190f )
            return false;

        var forward = player.Controller.IsValid()
            ? player.Controller.EyeTransform.Forward
            : player.WorldRotation.Forward;

        var direction = (target.WorldPosition - player.WorldPosition).Normal;
        return Vector3.Dot( forward.Normal, direction ) > 0.35f;
    }

    private static Player FindPlayerBySteamId( long steamId )
    {
        var scene = Game.ActiveScene;
        if ( scene is null )
            return null;

        foreach ( var player in scene.GetAllComponents<Player>() )
        {
            if ( player.GameObject.Network.Owner?.SteamId.Value == steamId )
                return player;
        }

        return null;
    }

    private static string GetConnectionName( Connection connection )
    {
        return string.IsNullOrWhiteSpace( connection?.DisplayName ) ? "игроку" : connection.DisplayName;
    }

    private static void NotifyMoneyResult( Connection connection, string message, bool success )
    {
        if ( connection is null )
            return;

        using ( Rpc.FilterInclude( c => c.SteamId.Value == connection.SteamId.Value ) )
        {
            RpcReceiveMoneyResult( message, success );
        }
    }

    [Rpc.Broadcast]
    private static void RpcReceiveMoneyResult( string message, bool success )
    {
        if ( success )
            Notification.Info( message, 3.5f );
        else
            Notification.Error( message, 3.5f );
    }

    // ===================== ARREST SYSTEM =====================

    /// <summary>
    /// Локально применяет/снимает эффекты ареста и инициирует автосамоосвобождение
    /// после истечения таймера (клиент шлёт запрос хосту).
    /// </summary>
    private void UpdateArrestEffects()
    {
        if (Controller.IsValid())
        {
            if (IsArrested && !_arrestSpeedApplied)
            {
                _origWalkSpeed = Controller.WalkSpeed;
                _origRunSpeed = Controller.RunSpeed;
                Controller.WalkSpeed = _origWalkSpeed * 0.5f;
                Controller.RunSpeed = _origRunSpeed * 0.5f;
                _arrestSpeedApplied = true;
            }
            else if (!IsArrested && _arrestSpeedApplied)
            {
                Controller.WalkSpeed = _origWalkSpeed;
                Controller.RunSpeed = _origRunSpeed;
                _arrestSpeedApplied = false;
            }
        }

        if (IsProxy) return;

        if (IsArrested)
        {
            // Арестованный не должен держать оружие в руках.
            if (CurrentWeapon.IsValid())
            {
                CurrentWeapon.GameObject.Enabled = false;
                CurrentWeapon = null;
                CurrentInventorySlotIndex = -1;
                CurrentWeaponItemId = null;
                RpcSetHoldType(Renderer, 0);
            }

            // Время считаем на клиенте; по истечении просим хост освободить.
            if ((float)ArrestTimeUntilRelease <= 0f)
                RequestSelfRelease();
        }
    }

    /// <summary>Локальный игрок инициирует арест цели через хост.</summary>
    public void RequestArrestTarget(GameObject targetObj)
    {
        if (!targetObj.IsValid()) return;
        RpcHostArrestTarget(targetObj);
    }

    /// <summary>Локальный игрок инициирует освобождение цели через хост.</summary>
    public void RequestReleaseTarget(GameObject targetObj)
    {
        if (!targetObj.IsValid()) return;
        RpcHostReleaseTarget(targetObj);
    }

    /// <summary>Локальный арестованный игрок просит хост освободить себя по истечении таймера.</summary>
    public void RequestSelfRelease()
    {
        if (IsProxy) return;
        if (!IsArrested) return;
        RpcHostSelfRelease();
    }

    [Rpc.Host]
    private void RpcHostArrestTarget(GameObject targetObj)
    {
        if (!Networking.IsHost) return;

        var caller = Rpc.Caller;
        if (caller is null) return;

        var attacker = FindPlayerBySteamId(caller.SteamId.Value);
        if (!attacker.IsValid()) return;
        if (!targetObj.IsValid()) return;
        if (!targetObj.Components.TryGet<Player>(out var target, FindMode.EverythingInSelfAndParent)) return;
        if (target == attacker) return;
        if (target.IsArrested) return;

        if (Vector3.DistanceBetween(attacker.WorldPosition, target.WorldPosition) > target.ArrestInteractRange)
            return;

        target.HostArrest();
    }

    [Rpc.Host]
    private void RpcHostReleaseTarget(GameObject targetObj)
    {
        if (!Networking.IsHost) return;

        var caller = Rpc.Caller;
        if (caller is null) return;

        var attacker = FindPlayerBySteamId(caller.SteamId.Value);
        if (!attacker.IsValid()) return;
        if (!targetObj.IsValid()) return;
        if (!targetObj.Components.TryGet<Player>(out var target, FindMode.EverythingInSelfAndParent)) return;
        if (!target.IsArrested) return;

        if (Vector3.DistanceBetween(attacker.WorldPosition, target.WorldPosition) > target.ArrestInteractRange)
            return;

        target.HostRelease();
    }

    [Rpc.Host]
    private void RpcHostSelfRelease()
    {
        if (!Networking.IsHost) return;

        var caller = Rpc.Caller;
        if (caller is null) return;

        var p = FindPlayerBySteamId(caller.SteamId.Value);
        if (!p.IsValid() || p != this) return;
        if (!p.IsArrested) return;

        p.HostRelease();
    }

    /// <summary>Хост: переводит игрока в состояние ареста и телепортирует к точке спавна тюрьмы.</summary>
    private void HostArrest()
    {
        if (!Networking.IsHost) return;
        if (IsArrested) return;

        IsArrested = true;
        ArrestTimeUntilRelease = ArrestDurationSeconds;

        var pos = ArrestSpawnPoint.IsValid() ? ArrestSpawnPoint.WorldPosition : WorldPosition;
        var rot = ArrestSpawnPoint.IsValid() ? ArrestSpawnPoint.WorldRotation : WorldRotation;
        RpcApplyArrest(pos, rot);
    }

    /// <summary>Хост: снимает арест и просит клиент респавнуться на обычной точке.</summary>
    private void HostRelease()
    {
        if (!Networking.IsHost) return;
        if (!IsArrested) return;

        IsArrested = false;
        ArrestTimeUntilRelease = 0f;
        RpcApplyRelease();
    }

    [Rpc.Owner]
    private void RpcApplyArrest(Vector3 pos, Rotation rot)
    {
        if (Controller.IsValid())
        {
            WorldPosition = pos;
            Controller.EyeAngles = rot;
        }
    }

    [Rpc.Owner]
    private void RpcApplyRelease()
    {
        SpawnInternal();
    }
}
