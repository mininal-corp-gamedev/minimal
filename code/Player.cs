using Ambi.Storage;
using Ambi.Utils;
using Minimal.ItemUseHandlers;
using Sandbox;
using System;
using System.Threading.Tasks;
using System.Text.Json.Serialization;

public sealed class Player : Component, ICustomDamagable, PlayerController.IEvents
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

    /// <summary>Maximum number of spawned props this player can own at once.</summary>
    [Property] public int MaxProps { get; set; } = 20;

    [Sync(SyncFlags.FromHost)] public float Health { get; set; } = 100f;
    [Sync(SyncFlags.FromHost)] public float MaxHealth { get; set; } = 100f;
    [Sync(SyncFlags.FromHost)] public bool IsDead { get; private set; }
    [Sync(SyncFlags.FromHost)] public TimeUntil DeathTimeUntilRespawn { get; private set; }
    [Sync(SyncFlags.FromHost)] public string DeathMessage { get; private set; } = "";

    [Property, Category("Death")] public float RespawnDelaySeconds { get; set; } = 5f;
    [Property, Category("Fall Damage")] public float SafeFallDistance { get; set; } = 420f;
    [Property, Category("Fall Damage")] public float FatalFallDistance { get; set; } = 1150f;
    [Property, Category("Fall Damage")] public float FatalFallDamage { get; set; } = 120f;
    [Property, Category("Fall Damage")] public float FallDamageSpawnGraceSeconds { get; set; } = 1.5f;

    private int _money;
    [Sync(SyncFlags.FromHost)]
    public int Money
    {
        get => _money;
        set
        {
            if (_money == value) return;
            _money = value;
            if (Networking.IsHost && _saveInitialized)
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
    private const string InventorySaveFolder = "inv";
    private const int DefaultStartingMoney = 500;
    private const int InventorySlotCount = 20;
    private static readonly string[] DefaultInventoryItemIds = { "hands", "physgun", "toolgun", "keys" };

    // Host-only gate. Until the save is loaded on the host, Money writes
    // must not overwrite the file on disk.
    private bool _saveInitialized;
    private bool _inventorySaveInitialized;
    private bool _inventoryEventsHooked;
    private bool _deferInventorySync;
    private bool _inventoryChangedWhileDeferred;
    private readonly List<PropCustom> _ownedPropSpawnStack = new();
    private bool _ignoreNextFallDamage = true;
    private TimeUntil _fallDamageGraceUntil;
    private TimeUntil _nextFallDamageAllowed;
    private GameObject _deathRagdollObject;
    private bool _deathControlsApplied;
    private bool _deathPrevUseInputControls;
    private bool _deathPrevUseLookControls;
    private bool _deathPrevUseCameraControls;
    private bool _deathColliderApplied;
    private bool _deathPrevColliderEnabled;
    private Vector3 _queuedElevatorCarryDelta;
    private bool _ownerClothingApplyInProgress;
    private bool _ownerClothingApplied;
    private TimeUntil _nextOwnerClothingApplyAttempt = 0f;
    private static bool _jobInventoryEventsRegistered;

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
    public bool IsAlive => Health > 0 && !IsDead;
    public Inventory Inventory { get; set; } = new(InventorySlotCount);

    public Weapon CurrentWeapon { get; private set; }
    public int CurrentInventorySlotIndex { get; private set; } = -1;
    public int SelectedHotbarSlotIndex { get; private set; } = -1;
    public string CurrentWeaponItemId { get; private set; }
    [Sync(SyncFlags.FromHost)] public string EquippedWeaponItemId { get; private set; } = "";

    public bool IsLocalPlayer => !IsProxy;

    [Sync] public bool PhysgunBeamActive { get; private set; }
    [Sync] public Vector3 PhysgunBeamStart { get; private set; }
    [Sync] public Vector3 PhysgunBeamEnd { get; private set; }
    [Sync] public Vector3 PhysgunBeamBend { get; private set; }

    private static bool _itemUseHandlersRegistered;
    private GameObject _worldWeaponObject;
    private string _worldWeaponItemId;
    private string _worldWeaponFailedItemId;
    private bool _worldWeaponUsesAuthoredPrefab;

    private sealed class WorldWeaponVisualDefinition
    {
        public Vector3 PositionOffset { get; }
        public Rotation RotationOffset { get; }
        public float Scale { get; }

        public WorldWeaponVisualDefinition(Vector3 positionOffset, Rotation rotationOffset, float scale = 1f)
        {
            PositionOffset = positionOffset;
            RotationOffset = rotationOffset;
            Scale = scale;
        }
    }

    private static readonly Dictionary<string, WorldWeaponVisualDefinition> WorldWeaponVisuals = new(StringComparer.OrdinalIgnoreCase)
    {
        ["usp"] = new(new Vector3(-17f, 2f, 0f), Rotation.Identity, 0.9f),
        ["mp5"] = new(new Vector3(-24f, 3f, 0f), Rotation.Identity, 0.9f),
        ["m4a1"] = new(new Vector3(-29f, 3f, 0f), Rotation.Identity, 0.9f),
        ["physgun"] = new(new Vector3(-8f, 2f, 4f), Rotation.Identity, 0.65f),
        ["toolgun"] = new(new Vector3(-17f, 3f, 0f), Rotation.Identity, 0.9f),
        ["pickaxe"] = new(new Vector3(-5f, 0f, 0f), Rotation.Identity),
        ["picklock"] = new(new Vector3(-5f, 0f, 0f), Rotation.Identity),
        ["handcuff"] = new(new Vector3(-4f, 0f, 0f), Rotation.Identity, 0.75f)
    };

    private static readonly string[] WeaponVisualBoneNames =
    {
        "hold_R",
        "hand_R",
        "weapon_R",
        "ValveBiped.Bip01_R_Hand"
    };

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

        HostTriggerRespawn();
    }

    /// <summary>
    /// Хост: запросить респавн этого игрока. Если игрок принадлежит хосту — спавним
    /// прямо здесь; иначе шлём <see cref="RpcOwnerSpawn"/> владельцу, потому что
    /// <see cref="PlayerController"/> авторитетен на стороне владельца, и
    /// телепорт/смена угла камеры с хоста для прокси не «прилипают».
    /// </summary>
    public void HostTriggerRespawn()
    {
        if (!Networking.IsHost) return;

        if (!IsProxy)
        {
            SpawnInternal();
            RpcClearDeathRagdoll();
            return;
        }

        // Точку спавна выбираем на хосте, чтобы у владельца не было десинка.
        var spawnPoint = SpawnManager.Instance?.GetRandomPlayerSpawn();
        if (!spawnPoint.IsValid()) return;

        Health = MaxHealth;
        IsDead = false;
        DeathTimeUntilRespawn = 0f;
        DeathMessage = "";
        ResetFallDamageGrace();
        WorldHud?.WorldHudRefresh();
        Job?.NotifySpawned();

        RpcOwnerSpawn(spawnPoint.WorldPosition, spawnPoint.WorldRotation);
        RpcClearDeathRagdoll();
    }

    /// <summary>
    /// Хост: телепортировать игрока. Для прокси шлём RPC владельцу, потому что
    /// у host-authority transform-апдейты для не-host игрока перетираются
    /// движением контроллера на стороне владельца.
    /// </summary>
    public void HostTeleport(Vector3 position, Rotation rotation)
    {
        if (!Networking.IsHost) return;

        if (!IsProxy)
        {
            ClearQueuedElevatorCarryDelta();
            WorldPosition = position;
            if (Controller.IsValid())
                Controller.EyeAngles = rotation;
            return;
        }

        RpcOwnerTeleport(position, rotation);
    }

    public void ApplyElevatorCarryDelta(Vector3 delta)
    {
        if (!Networking.IsHost) return;
        if (delta.LengthSquared <= 0.000001f) return;

        if (!IsProxy)
        {
            ApplyElevatorDelta(delta);
            return;
        }

        RpcOwnerQueueElevatorCarryDelta(delta);
    }

    [Rpc.Owner]
    private void RpcOwnerQueueElevatorCarryDelta(Vector3 delta)
    {
        if (Networking.IsHost) return;
        if (IsProxy) return;
        if (delta.LengthSquared <= 0.000001f) return;

        _queuedElevatorCarryDelta += delta;
    }

    [Rpc.Owner]
    private void RpcOwnerSpawn(Vector3 position, Rotation rotation)
    {
        if (Networking.IsHost) return;

        ClearQueuedElevatorCarryDelta();
        WorldPosition = position;
        if (Controller.IsValid())
            Controller.EyeAngles = rotation;
        IsDead = false;
        DeathTimeUntilRespawn = 0f;
        DeathMessage = "";
        RestoreDeathState();
        ResetFallDamageGrace();
        WorldHud?.WorldHudRefresh();
    }

    [Rpc.Owner]
    private void RpcOwnerTeleport(Vector3 position, Rotation rotation)
    {
        if (Networking.IsHost) return;

        ClearQueuedElevatorCarryDelta();
        WorldPosition = position;
        if (Controller.IsValid())
            Controller.EyeAngles = rotation;
    }

    private void ApplyElevatorDelta(Vector3 delta)
    {
        if (!Controller.IsValid() || IsDead || IsArrested)
            return;

        WorldPosition += delta;
        ResetElevatorFallDamageGrace();

        if (!Controller.Body.IsValid())
            return;

        var velocity = Controller.Body.Velocity;
        if (MathF.Abs(delta.z) > 0.001f && velocity.z < 0f)
            velocity.z = 0f;

        Controller.Body.Velocity = velocity;
    }

    private void ApplyQueuedElevatorCarryDelta()
    {
        if (_queuedElevatorCarryDelta.LengthSquared <= 0.000001f)
            return;

        var delta = _queuedElevatorCarryDelta;
        _queuedElevatorCarryDelta = Vector3.Zero;
        ApplyElevatorDelta(delta);
    }

    private void ClearQueuedElevatorCarryDelta()
    {
        _queuedElevatorCarryDelta = Vector3.Zero;
    }

    private void SpawnInternal()
    {
        var spawnPoint = SpawnManager.Instance?.GetRandomPlayerSpawn();
        if (!spawnPoint.IsValid()) return;

        Health = MaxHealth;
        IsDead = false;
        DeathTimeUntilRespawn = 0f;
        DeathMessage = "";
        RestoreDeathState();
        ResetFallDamageGrace();
        WorldHud?.WorldHudRefresh();
        ClearQueuedElevatorCarryDelta();
        WorldPosition = spawnPoint.WorldPosition;
        Controller.EyeAngles = spawnPoint.WorldRotation;

        Job?.NotifySpawned();
    }

    public void OnDamage(in DamageInfo dmgInfo)
    {
        // Server (host) authority: урон применяет ТОЛЬКО хост; клиент только просит.
        if (IsArrested) return;

        TakeDamageFromWeapon(dmgInfo.Damage, dmgInfo.Attacker);
    }

    /// <summary>
    /// Запрос на урон. Применять Health может только хост (он — Sync-владелец).
    /// На хосте применяем сразу, на клиенте отправляем <see cref="RpcRequestDamage"/>.
    /// </summary>
    public void TakeDamageFromWeapon(float damage, GameObject attacker = null, string deathMessage = null)
    {
        if (damage <= 0f) return;

        if (Networking.IsHost)
        {
            HostApplyDamage(damage, attacker, deathMessage);
            return;
        }

        RpcRequestDamage(damage, attacker, deathMessage);
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

    /// <summary>
    /// Запрос урона от клиента к хосту. Только хост авторитетен по
    /// <see cref="Health"/> (<c>Sync(SyncFlags.FromHost)</c>), поэтому клиент
    /// не может писать здоровье сам — иначе изменение не разойдётся по сети
    /// (что и было причиной «клиент не дамажит клиента»).
    /// </summary>
    [Rpc.Host]
    private void RpcRequestDamage(float damage, GameObject attacker, string deathMessage)
    {
        if (!Networking.IsHost) return;
        HostApplyDamage(damage, attacker, deathMessage);
    }

    private void HostApplyDamage(float damage, GameObject attacker, string deathMessage = null)
    {
        if (!Networking.IsHost) return;
        if (IsArrested) return;
        if (damage <= 0f) return;
        if (Health <= 0f || IsDead) return;

        // Само-урон через одно и то же оружие/трейс невозможен (трейс игнорирует
        // владельца), но на всякий случай отбрасываем явный self-hit.
        if (attacker.IsValid() && attacker == GameObject) return;

        Health = Math.Max(0f, Health - damage);
        WorldHud?.WorldHudRefresh();

        RpcOnPlayerHit(Renderer);

        if (Health <= 0f)
            HostDie(BuildDeathMessage(attacker, deathMessage));
    }

    /// <summary>Смерть. Хост показывает владельцу экран смерти и откладывает респавн.</summary>
    private void HostDie(string deathMessage = null)
    {
        if (!Networking.IsHost) return;
        if (IsDead) return;

        ClearQueuedElevatorCarryDelta();
        IsDead = true;
        Health = 0f;
        DeathTimeUntilRespawn = MathF.Max(0.1f, RespawnDelaySeconds);
        DeathMessage = string.IsNullOrWhiteSpace(deathMessage) ? "Вы умерли." : deathMessage;
        HostSetEquippedWeaponItemId(null);
        WorldHud?.WorldHudRefresh();

        RpcOwnerDied(DeathMessage, (float)DeathTimeUntilRespawn);
        RpcCreateDeathRagdoll();
    }

    [Rpc.Owner]
    private void RpcOwnerDied(string deathMessage, float respawnDelay)
    {
        ClearQueuedElevatorCarryDelta();
        IsDead = true;
        Health = 0f;
        DeathTimeUntilRespawn = MathF.Max(0.1f, respawnDelay);
        DeathMessage = string.IsNullOrWhiteSpace(deathMessage) ? "Вы умерли." : deathMessage;

        if (CurrentWeapon.IsValid())
            CurrentWeapon.GameObject.Enabled = false;

        ApplyDeathControls();
    }

    [Rpc.Broadcast]
    private void RpcCreateDeathRagdoll()
    {
        CreateDeathRagdoll();
    }

    [Rpc.Broadcast]
    private void RpcClearDeathRagdoll()
    {
        RestoreDeathState();
    }

    private void HostUpdateDeathRespawn()
    {
        if (!Networking.IsHost) return;
        if (!IsDead) return;
        if ((float)DeathTimeUntilRespawn > 0f) return;

        HostTriggerRespawn();
    }

    private static string BuildDeathMessage(GameObject attacker, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(fallback))
            return fallback;

        var attackerName = GetAttackerDisplayName(attacker);
        if (!string.IsNullOrWhiteSpace(attackerName))
            return $"Вас убил - \"{attackerName}\"";

        return "Вы умерли.";
    }

    private static string GetAttackerDisplayName(GameObject attacker)
    {
        if (!attacker.IsValid())
            return null;

        var go = attacker;
        while (go.IsValid())
        {
            if (go.Components.TryGet<Player>(out var player, FindMode.EverythingInSelfAndParent))
            {
                var ownerName = player.GameObject.Network.Owner?.DisplayName;
                if (!string.IsNullOrWhiteSpace(ownerName))
                    return ownerName;
            }

            go = go.Parent;
        }

        return null;
    }

    private void CreateDeathRagdoll()
    {
        DestroyDeathRagdoll();

        if (CurrentWeapon.IsValid())
            CurrentWeapon.GameObject.Enabled = false;

        if (!IsProxy)
            ApplyDeathControls();

        if (!Controller.IsValid())
            return;

        _deathRagdollObject = Controller.CreateRagdoll($"{GameObject.Name}_ragdoll");
        ApplyDeathColliderState();

        if (Renderer.IsValid())
            Renderer.Enabled = false;
    }

    private void ApplyDeathColliderState()
    {
        if (_deathColliderApplied || !Controller.IsValid() || !Controller.ColliderObject.IsValid())
            return;

        _deathPrevColliderEnabled = Controller.ColliderObject.Enabled;
        Controller.ColliderObject.Enabled = false;
        _deathColliderApplied = true;
    }

    private void ApplyDeathControls()
    {
        if (_deathControlsApplied || !Controller.IsValid())
            return;

        _deathPrevUseInputControls = Controller.UseInputControls;
        _deathPrevUseLookControls = Controller.UseLookControls;
        _deathPrevUseCameraControls = Controller.UseCameraControls;

        Controller.UseInputControls = false;
        Controller.UseLookControls = false;
        Controller.UseCameraControls = false;
        Controller.WishVelocity = Vector3.Zero;

        if (Controller.Body.IsValid())
            Controller.Body.Velocity = Vector3.Zero;

        _deathControlsApplied = true;
    }

    private void RestoreDeathState()
    {
        DestroyDeathRagdoll();

        if (Renderer.IsValid())
            Renderer.Enabled = true;

        RestoreDeathColliderState();
        RestoreDeathControls();

        if (CurrentWeapon.IsValid() && IsAlive && !IsArrested)
            CurrentWeapon.GameObject.Enabled = true;
    }

    private void RestoreDeathColliderState()
    {
        if (!_deathColliderApplied || !Controller.IsValid() || !Controller.ColliderObject.IsValid())
            return;

        Controller.ColliderObject.Enabled = _deathPrevColliderEnabled;
        _deathColliderApplied = false;
    }

    private void RestoreDeathControls()
    {
        if (!_deathControlsApplied || !Controller.IsValid())
            return;

        Controller.UseInputControls = _deathPrevUseInputControls;
        Controller.UseLookControls = _deathPrevUseLookControls;
        Controller.UseCameraControls = _deathPrevUseCameraControls;
        _deathControlsApplied = false;
    }

    private void DestroyDeathRagdoll()
    {
        if (_deathRagdollObject.IsValid())
            _deathRagdollObject.Destroy();

        _deathRagdollObject = null;
    }

    public void OnLanded(float distance, Vector3 impactVelocity)
    {
        if (IsArrested || IsDead || Health <= 0f) return;
        if (distance <= SafeFallDistance) return;
        if (!Networking.IsHost && IsProxy) return;

        if (Networking.IsHost)
        {
            HostApplyFallDamage(distance);
            return;
        }

        RpcRequestFallDamage(distance);
    }

    [Rpc.Host]
    private void RpcRequestFallDamage(float distance)
    {
        if (!Networking.IsHost) return;

        var caller = Rpc.Caller;
        if (caller is null) return;

        var player = FindPlayerBySteamId(caller.SteamId.Value);
        if (!player.IsValid() || player != this) return;

        player.HostApplyFallDamage(distance);
    }

    private void HostApplyFallDamage(float distance)
    {
        if (!Networking.IsHost) return;
        if (!_nextFallDamageAllowed) return;
        if (IsArrested || IsDead || Health <= 0f) return;
        if (ShouldIgnoreFallDamage()) return;

        var damage = CalculateFallDamage(distance);
        if (damage <= 0f) return;

        _nextFallDamageAllowed = 0.2f;
        HostApplyDamage(damage, null, "Вы умерли от падения с высоты.");
    }

    private float CalculateFallDamage(float distance)
    {
        var safeDistance = MathF.Max(0f, SafeFallDistance);
        var fatalDistance = MathF.Max(safeDistance + 1f, FatalFallDistance);
        var clampedDistance = Math.Clamp(distance, safeDistance, fatalDistance);
        var fallPercent = (clampedDistance - safeDistance) / (fatalDistance - safeDistance);

        return MathF.Ceiling(fallPercent * MathF.Max(0f, FatalFallDamage));
    }

    private void ResetFallDamageGrace()
    {
        _ignoreNextFallDamage = true;
        _fallDamageGraceUntil = MathF.Max(0f, FallDamageSpawnGraceSeconds);
        _nextFallDamageAllowed = 0.2f;
    }

    private void ResetElevatorFallDamageGrace()
    {
        _ignoreNextFallDamage = true;
        _fallDamageGraceUntil = 0.35f;
        _nextFallDamageAllowed = 0.2f;
    }

    private bool ShouldIgnoreFallDamage()
    {
        if (_ignoreNextFallDamage)
        {
            _ignoreNextFallDamage = false;
            return true;
        }

        return (float)_fallDamageGraceUntil > 0f;
    }

    /// <summary>Совместимость со старым API (вызывалось локально владельцем).</summary>
    public void Die()
    {
        if (Networking.IsHost)
        {
            HostDie();
            return;
        }

        if (IsProxy) return;
        RpcRequestDie();
    }

    [Rpc.Host]
    private void RpcRequestDie()
    {
        if (!Networking.IsHost) return;

        var caller = Rpc.Caller;
        if (caller is null) return;

        var player = FindPlayerBySteamId(caller.SteamId.Value);
        if (!player.IsValid() || player != this) return;

        player.HostDie();
    }

    public void SwitchWeapon(Weapon wep = null)
    {
        if (IsProxy) return;
        if (!IsAlive && wep.IsValid()) return;
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

    private void HostSetEquippedWeaponItemId(string itemId)
    {
        if (!Networking.IsHost)
            return;

        EquippedWeaponItemId = string.IsNullOrWhiteSpace(itemId) ? "" : itemId;
    }

    public bool UseInventorySlot(int slotIndex)
    {
        if (IsProxy) return false;
        if (!IsAlive) return false;
        if (IsArrested) return false; // Арестованный не может пользоваться предметами

        if (Networking.IsHost)
            return HostUseInventorySlot(slotIndex);

        // Оптимистичный локальный апдейт подсветки слота: без него хотбар
        // визуально «лагает» на величину сетевого RTT, пока не придёт
        // RpcOwnerUseInventorySlotApproved. Если хост слот отвергнет —
        // approved-RPC не придёт, индикатор просто останется на старом значении
        // (валидируется ValidateCurrentWeaponInventoryState на клиенте).
        if (slotIndex >= 0 && slotIndex < 9)
            SelectedHotbarSlotIndex = slotIndex;

        RpcRequestUseInventorySlot(slotIndex);
        return true;
    }

    public int OwnedPropsCount
    {
        get
        {
            var count = 0;

            foreach ( var go in Scene.GetAllObjects( true ) )
            {
                if ( !go.Components.TryGet<PropCustom>( out var prop ) )
                    continue;

                if ( prop.PlayerOwner == this )
                    count++;
            }

            return count;
        }
    }

    [Rpc.Host]
    private void RpcRequestUseInventorySlot(int slotIndex)
    {
        if (!Networking.IsHost)
            return;

        var player = FindCallerPlayer();
        if (!player.IsValid() || player != this)
            return;

        player.HostUseInventorySlot(slotIndex);
    }

    private bool HostUseInventorySlot(int slotIndex)
    {
        if (!Networking.IsHost) return false;
        if (!_inventorySaveInitialized) return false;
        if (IsArrested) return false;
        if (Inventory == null) return false;
        if (slotIndex < 0 || slotIndex >= Inventory.Slots.Count) return false;

        if (slotIndex >= 0 && slotIndex < 9)
            SelectedHotbarSlotIndex = slotIndex;

        var slot = Inventory.Slots[slotIndex];
        if (slot.IsEmpty || slot.Item == null)
        {
            HostSetEquippedWeaponItemId(null);

            if (CurrentWeapon.IsValid())
                SwitchWeapon();

            RpcOwnerUseInventorySlotApproved(slotIndex, null);
            return true;
        }

        if (slot.Item.Definition is null || !slot.Item.Definition.CanUse)
            return false;

        var itemId = slot.Item.Id;
        var isWeapon = IsWeaponItem(slot.Item);
        bool successful;
        _deferInventorySync = true;
        try
        {
            successful = Inventory.TryUseItem(slot, this);
        }
        finally
        {
            _deferInventorySync = false;
        }

        if (successful && isWeapon)
        {
            CurrentInventorySlotIndex = slotIndex;
            CurrentWeaponItemId = itemId;
            HostSetEquippedWeaponItemId(itemId);
        }

        ValidateCurrentWeaponInventoryState();

        if (successful)
            RpcOwnerUseInventorySlotApproved(slotIndex, itemId);
        if (_inventoryChangedWhileDeferred)
        {
            _inventoryChangedWhileDeferred = false;
            SavePlayerInventory();
            SendInventorySnapshotToOwner();
        }
        else if (!successful)
        {
            SendInventorySnapshotToOwner();
        }

        return successful;
    }

    [Rpc.Owner]
    private void RpcOwnerUseInventorySlotApproved(int slotIndex, string itemId)
    {
        if (Networking.IsHost)
            return;
        if (Inventory == null) return;
        if (slotIndex < 0 || slotIndex >= Inventory.Slots.Count) return;

        if (slotIndex >= 0 && slotIndex < 9)
            SelectedHotbarSlotIndex = slotIndex;

        if (string.IsNullOrWhiteSpace(itemId))
        {
            SwitchWeapon();
            return;
        }

        var slot = Inventory.Slots[slotIndex];
        if (slot.IsEmpty || slot.Item == null || slot.Item.Id != itemId)
            return;

        var item = slot.Item;
        var isWeapon = IsWeaponItem(item);
        var successful = Inventory.TryUseItem(slot, this);

        if (successful && isWeapon)
        {
            CurrentInventorySlotIndex = slotIndex;
            CurrentWeaponItemId = itemId;
        }

        ValidateCurrentWeaponInventoryState();
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
        if (!IsAlive) return;
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
        HostSetEquippedWeaponItemId(null);
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

    public bool MoveOrSwapInventorySlots(int fromIndex, int toIndex)
    {
        if (IsProxy) return false;

        if (Networking.IsHost)
            return HostMoveOrSwapInventorySlots(fromIndex, toIndex);

        RpcRequestMoveOrSwapInventorySlots(fromIndex, toIndex);
        return true;
    }

    [Rpc.Host]
    private void RpcRequestMoveOrSwapInventorySlots(int fromIndex, int toIndex)
    {
        if (!Networking.IsHost)
            return;

        var player = FindCallerPlayer();
        if (!player.IsValid() || player != this)
            return;

        player.HostMoveOrSwapInventorySlots(fromIndex, toIndex);
    }

    private bool HostMoveOrSwapInventorySlots(int fromIndex, int toIndex)
    {
        if (!Networking.IsHost) return false;
        if (!_inventorySaveInitialized) return false;
        if (Inventory is null) return false;

        var successful = Inventory.TryMoveOrSwap(fromIndex, toIndex);
        if (!successful)
            SendInventorySnapshotToOwner();

        return successful;
    }

    public void DropItem(Slot slot, int count = 1)
    {
        var slotIndex = Inventory?.GetIndex(slot);
        if (!slotIndex.HasValue)
            return;

        DropInventorySlot(slotIndex.Value, count);
    }

    public void DropInventorySlot(int slotIndex, int count = 1)
    {
        if (IsProxy) return;

        if (Networking.IsHost)
        {
            HostDropInventorySlot(slotIndex, count);
            return;
        }

        RpcRequestDropInventorySlot(slotIndex, count);
    }

    [Rpc.Host]
    private void RpcRequestDropInventorySlot(int slotIndex, int count)
    {
        if (!Networking.IsHost)
            return;

        var player = FindCallerPlayer();
        if (!player.IsValid() || player != this)
            return;

        player.HostDropInventorySlot(slotIndex, count);
    }

    private bool HostDropInventorySlot(int slotIndex, int count)
    {
        if (!Networking.IsHost) return false;
        if (!_inventorySaveInitialized) return false;
        if (count <= 0) return false;
        if (Inventory == null) return false;
        if (slotIndex < 0 || slotIndex >= Inventory.Slots.Count) return false;
        var slot = Inventory.Slots[slotIndex];
        if (slot.IsEmpty || slot.Item == null) return false;
        if (slot.Item.Count < count) return false;
        if (!slot.Item.CanDrop)
        {
            NotifyInventoryResult(GameObject.Network.Owner, "Этот предмет нельзя выбросить.", false);
            return false;
        }
        if (!ItemDropPrefab.IsValid()) return false;

        var item = slot.Item;

        var pos = WorldPosition + Controller.EyeTransform.Forward * 90f + Controller.EyeTransform.Up * 80f; // Slightly above so it doesn't get stuck in ground
        var gameObj = ItemDropPrefab.Clone(pos);
        var itemComponent = gameObj.GetComponent<ItemComponent>();
        if (!itemComponent.IsValid())
        {
            gameObj.Destroy();
            return false;
        }

        itemComponent.Count = count;
        itemComponent.ItemDefinition = item.Definition;
        itemComponent.CanDrop = item.CanDrop;
        itemComponent.IsJobItem = item.IsJobItem;
        itemComponent.CanSave = item.CanSave;
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

        NotifyInventoryResult(GameObject.Network.Owner, $"You dropped: {item.Definition.Header}", true);
        return true;
    }

    public bool HostAddItem(Item item)
    {
        if (!Networking.IsHost) return false;
        if (!_inventorySaveInitialized) return false;
        if (Inventory is null || item is null) return false;
        if (!Inventory.CanAddItem(item)) return false;

        return Inventory.AddItem(item);
    }

    public bool HostGiveJobItem(string itemId, int count = 1, bool canDrop = true, bool canSave = false)
    {
        return HostAddItem(Item.Create(itemId, count, canDrop, isJobItem: true, canSave: canSave));
    }

    public int HostRemoveJobItems()
    {
        if (!Networking.IsHost) return 0;
        if (!_inventorySaveInitialized || Inventory is null) return 0;

        return Inventory.RemoveJobItems();
    }

    public int HostTryPickup(ItemComponent droppedItem)
    {
        if (!Networking.IsHost) return 0;
        if (!_inventorySaveInitialized) return 0;
        if (!droppedItem.IsValid()) return 0;
        if (Inventory is null || droppedItem.ItemDefinition is null) return 0;
        if (droppedItem.Count <= 0)
        {
            droppedItem.GameObject.Destroy();
            return 0;
        }

        if (Vector3.DistanceBetween(WorldPosition, droppedItem.WorldPosition) > droppedItem.PickupRadius)
            return 0;

        var itemName = string.IsNullOrWhiteSpace(droppedItem.ItemDefinition.Header)
            ? droppedItem.ItemDefinition.Id
            : droppedItem.ItemDefinition.Header;

        var taken = droppedItem.TryPickup(Inventory);
        if (taken <= 0)
        {
            SendInventorySnapshotToOwner();
            return 0;
        }

        NotifyInventoryResult(GameObject.Network.Owner, $"Picked up {itemName} x{taken}", true);
        return taken;
    }

    private void SetupWorldHud()
    {
        WorldHud.Name = Connection.Local.DisplayName;
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
        ItemUseRegistry.Register("keys", new WepKeysUseHandler());
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

        HookInventoryEvents();
        Job?.AssignDefault();
        Spawn();
        SetupWorldHud();
        AdminManager.RpcRequestRankInit();

        // Сейв-инит запрашиваем у хоста ИЗ NetworkInit, а не в OnStart.
        // OnStart на хосте может срабатывать раньше, чем GameObject.Network.Owner
        // успевает быть выставлен для клиентского Player — тогда
        // GetOwnerSteamId() возвращает 0 и HostInitSave молча выходил, из‑за
        // чего сохранение писалось, но не «выдавалось» при заходе.
        // Через Rpc.Host мы гарантированно знаем Rpc.Caller.SteamId на хосте.
        RpcRequestPlayerSaveInit();
        RpcRequestPlayerInventoryInit();
    }

    private void TryApplyOwnerClothing()
    {
        if (_ownerClothingApplied || _ownerClothingApplyInProgress)
            return;
        if (!_nextOwnerClothingApplyAttempt)
            return;
        if (!Dresser.IsValid())
            return;
        if (GameObject.Network.Owner is null)
            return;

        _ = ApplyOwnerClothingAsync();
    }

    private async Task ApplyOwnerClothingAsync()
    {
        _ownerClothingApplyInProgress = true;

        try
        {
            Dresser.Source = Sandbox.Dresser.ClothingSource.OwnerConnection;
            await Dresser.Apply();
            _ownerClothingApplied = true;
        }
        catch (Exception ex)
        {
            _nextOwnerClothingApplyAttempt = 2f;
            Log.Warning($"[Player] Failed to apply owner clothing for {GameObject.Network.Owner?.DisplayName ?? "unknown"}: {ex.Message}");
        }
        finally
        {
            _ownerClothingApplyInProgress = false;
        }
    }

    private void HookInventoryEvents()
    {
        if (_inventoryEventsHooked || Inventory is null)
            return;

        Inventory.OnChanged += ValidateCurrentWeaponInventoryState;

        if (Networking.IsHost)
            Inventory.OnChanged += HostOnInventoryChanged;

        _inventoryEventsHooked = true;
    }

    private void UnhookInventoryEvents()
    {
        if (!_inventoryEventsHooked || Inventory is null)
            return;

        Inventory.OnChanged -= ValidateCurrentWeaponInventoryState;
        Inventory.OnChanged -= HostOnInventoryChanged;
        _inventoryEventsHooked = false;
    }

    private static void RegisterJobInventoryEvents()
    {
        if (_jobInventoryEventsRegistered)
            return;

        PlayerJob.OnJobChanged += (player, _) => player?.HostRemoveJobItems();
        PlayerJob.OnJobDemote += player => player?.HostRemoveJobItems();
        _jobInventoryEventsRegistered = true;
    }

    private void HostOnInventoryChanged()
    {
        if (!Networking.IsHost)
            return;
        if (!_inventorySaveInitialized)
            return;
        if (_deferInventorySync)
        {
            _inventoryChangedWhileDeferred = true;
            return;
        }

        SavePlayerInventory();
        SendInventorySnapshotToOwner();
    }

    private long GetOwnerSteamId()
    {
        return GameObject.Network.Owner?.SteamId.Value ?? 0L;
    }

    private static string GetPlayerSavePath( long steamId ) => $"{PlayerSaveFolder}/{steamId}.json";
    private static string GetInventorySavePath( long steamId ) => $"{InventorySaveFolder}/{steamId}.json";

    private static void EnsurePlayerSaveFolder()
    {
        FileSystem.Data.CreateDirectory( PlayerSaveFolder );
    }

    private static void EnsureInventorySaveFolder()
    {
        FileSystem.Data.CreateDirectory( InventorySaveFolder );
    }

    /// <summary>
    /// Клиент-сторона: просит хост инициализировать денежный сейв ИМЕННО
    /// для этого подключения. На хосте по <see cref="Rpc.Caller"/> находим
    /// Player и грузим его сохранение (или выдаём стартовые деньги новичку).
    /// Это безопаснее, чем грузить в OnStart: к моменту RPC у Player уже
    /// гарантированно проставлен <c>GameObject.Network.Owner</c>.
    /// </summary>
    [Rpc.Host]
    public static void RpcRequestPlayerSaveInit()
    {
        if ( !Networking.IsHost ) return;

        var caller = Rpc.Caller;
        if ( caller is null ) return;

        var player = FindPlayerBySteamId( caller.SteamId.Value );
        if ( !player.IsValid() )
        {
            Log.Warning( $"[PlayerSave] Init request: player not found for {caller.DisplayName} ({caller.SteamId.Value})." );
            return;
        }

        if ( player._saveInitialized ) return;

        player.HostInitSave();
    }

    /// <summary>Клиент-сторона: просит хост инициализировать инвентарный сейв.</summary>
    [Rpc.Host]
    public static void RpcRequestPlayerInventoryInit()
    {
        if ( !Networking.IsHost ) return;

        var caller = Rpc.Caller;
        if ( caller is null ) return;

        var player = FindPlayerBySteamId( caller.SteamId.Value );
        if ( !player.IsValid() )
        {
            Log.Warning( $"[InventorySave] Init request: player not found for {caller.DisplayName} ({caller.SteamId.Value})." );
            return;
        }

        if ( player._inventorySaveInitialized ) return;

        player.HostInitInventorySave();
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

        // Сначала разрешаем персист, чтобы сеттер мог сохранять при изменениях.
        _saveInitialized = true;

        // Применяем через property-сеттер, а не в backing field напрямую.
        // [Sync(FromHost)] трекает изменение через property; запись в _money
        // напрямую не уведомляла клиентов, из-за чего Hud не показывал
        // загруженное значение после инициализации.
        Money = data.Money;

        // Гарантируем файл на диске даже если значение совпало с дефолтом
        // (тогда сеттер не вызвал бы SavePlayerData).
        SavePlayerData();
    }

    private void HostInitInventorySave()
    {
        if (!Networking.IsHost) return;

        var steamId = GetOwnerSteamId();
        if (steamId == 0L)
        {
            Log.Warning("[InventorySave] Cannot init inventory: owner SteamId is 0.");
            return;
        }

        HookInventoryEvents();
        EnsureInventorySaveFolder();

        InventorySnapshot snapshot = null;
        try
        {
            var path = GetInventorySavePath(steamId);
            if (FileSystem.Data.FileExists(path))
                snapshot = FileSystem.Data.ReadJsonOrDefault<InventorySnapshot>(path);
        }
        catch (Exception ex)
        {
            Log.Warning($"[InventorySave] Load failed for {steamId}: {ex.Message}");
        }

        if (snapshot is not null)
        {
            Inventory.ApplySnapshot(snapshot);
        }
        else
        {
            Inventory.ClearAll();
            Inventory.SetSlotCount(InventorySlotCount);
        }

        // Ensure all default items are present, even if loading from an existing save
        foreach (var itemId in DefaultInventoryItemIds)
        {
            if (Inventory.GetTotalCount(itemId) == 0)
                Inventory.AddItem(Item.Create(itemId, 1, canDrop: false, isJobItem: false, canSave: true));
        }

        _inventorySaveInitialized = true;
        SavePlayerInventory();
        SendInventorySnapshotToOwner();
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

    private void SavePlayerInventory()
    {
        if (!Networking.IsHost) return;
        if (!_inventorySaveInitialized) return;

        var steamId = GetOwnerSteamId();
        if (steamId == 0L) return;

        try
        {
            EnsureInventorySaveFolder();
            FileSystem.Data.WriteJson(GetInventorySavePath(steamId), Inventory.CreateSnapshot(steamId, includeNonSaveItems: false));
        }
        catch (Exception ex)
        {
            Log.Warning($"[InventorySave] Save failed for {steamId}: {ex.Message}");
        }
    }

    private void SendInventorySnapshotToOwner()
    {
        if (!Networking.IsHost || Inventory is null)
            return;

        RpcReceiveInventorySnapshot(Inventory.CreateSnapshotJson(GetOwnerSteamId()));
    }

    [Rpc.Owner]
    private void RpcReceiveInventorySnapshot(string snapshotJson)
    {
        if (Networking.IsHost)
            return;

        if (Inventory is null)
            Inventory = new Inventory(InventorySlotCount);
        Inventory.ApplySnapshotJson(snapshotJson);
        ValidateCurrentWeaponInventoryState();
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

    private void UpdateWorldWeaponVisual()
    {
        if (!IsProxy || IsArrested || string.IsNullOrWhiteSpace(EquippedWeaponItemId))
        {
            DestroyWorldWeaponVisual();
            return;
        }

        if (!WorldWeaponVisuals.TryGetValue(EquippedWeaponItemId, out var definition))
        {
            DestroyWorldWeaponVisual();
            return;
        }

        if (_worldWeaponFailedItemId == EquippedWeaponItemId)
        {
            if (!string.Equals(_worldWeaponItemId, EquippedWeaponItemId, StringComparison.OrdinalIgnoreCase))
                DestroyWorldWeaponVisual();
            return;
        }

        if (!EnsureWorldWeaponVisual(EquippedWeaponItemId, definition))
            return;

        UpdateWorldWeaponTransform(definition);
    }

    private bool EnsureWorldWeaponVisual(string itemId, WorldWeaponVisualDefinition definition)
    {
        if (_worldWeaponObject.IsValid()
            && string.Equals(_worldWeaponItemId, itemId, StringComparison.OrdinalIgnoreCase))
            return true;

        DestroyWorldWeaponVisual();

        var prefab = GetWorldWeaponPrefab(itemId, out var usesAuthoredPrefab);
        if (!prefab.IsValid())
        {
            _worldWeaponFailedItemId = itemId;
            Log.Warning($"[Player] World weapon prefab not found for '{itemId}'");
            return false;
        }

        _worldWeaponObject = prefab.Clone();
        _worldWeaponObject.Name = $"world_weapon_{itemId}";
        _worldWeaponObject.Parent = GameObject;
        _worldWeaponObject.LocalPosition = Vector3.Zero;
        _worldWeaponObject.LocalRotation = Rotation.Identity;
        _worldWeaponObject.LocalScale = Vector3.One * definition.Scale;

        PrepareWorldWeaponClone(_worldWeaponObject);

        _worldWeaponItemId = itemId;
        _worldWeaponFailedItemId = null;
        _worldWeaponUsesAuthoredPrefab = usesAuthoredPrefab;
        Log.Info($"[Player] World weapon visual '{itemId}' cloned from {(usesAuthoredPrefab ? "world" : "view")} prefab");
        return true;
    }

    private static GameObject GetWorldWeaponPrefab(string itemId, out bool usesAuthoredPrefab)
    {
        usesAuthoredPrefab = false;

        var manager = WeaponManager.Instance;
        if (!manager.IsValid())
            return null;

        var worldPrefab = itemId switch
        {
            "usp" => manager.WorldWeaponUspPrefab,
            "mp5" => manager.WorldWeaponMp5Prefab,
            "m4a1" => manager.WorldWeaponM4a1Prefab,
            "physgun" => manager.WorldWeaponPhysgun,
            "toolgun" => manager.WorldWeaponToolgun,
            "pickaxe" => manager.WorldWeaponPickaxe,
            "picklock" => manager.WorldWeaponPicklock,
            "handcuff" => manager.WorldWeaponHandcuff,
            _ => null
        };

        if (worldPrefab.IsValid())
        {
            usesAuthoredPrefab = true;
            return worldPrefab;
        }

        return itemId switch
        {
            "usp" => manager.WeaponUspPrefab,
            "mp5" => manager.WeaponMp5Prefab,
            "m4a1" => manager.WeaponM4a1Prefab,
            "physgun" => manager.WeaponPhysgun,
            "toolgun" => manager.WeaponToolgun,
            "pickaxe" => manager.WeaponPickaxe,
            "picklock" => manager.WeaponPicklock,
            "handcuff" => manager.WeaponHandcuff,
            _ => null
        };
    }

    private static void PrepareWorldWeaponClone(GameObject obj)
    {
        if (!obj.IsValid())
            return;

        if (obj.Components.TryGet<Weapon>(out var weapon))
            weapon.Enabled = false;

        DisableWorldWeaponArms(obj);
    }

    private static void DisableWorldWeaponArms(GameObject obj)
    {
        if (!obj.IsValid())
            return;

        if (string.Equals(obj.Name, "arms", StringComparison.OrdinalIgnoreCase))
            obj.Enabled = false;

        foreach (var child in obj.Children)
            DisableWorldWeaponArms(child);
    }

    private void UpdateWorldWeaponTransform(WorldWeaponVisualDefinition definition)
    {
        if (!_worldWeaponObject.IsValid() || !Renderer.IsValid())
            return;

        if (!TryGetWeaponVisualBaseTransform(out var transform))
            return;

        var positionOffset = _worldWeaponUsesAuthoredPrefab ? Vector3.Zero : definition.PositionOffset;
        var rotationOffset = _worldWeaponUsesAuthoredPrefab ? Rotation.Identity : definition.RotationOffset;
        var scale = _worldWeaponUsesAuthoredPrefab ? 1f : definition.Scale;

        _worldWeaponObject.WorldPosition = transform.Position + transform.Rotation * positionOffset;
        _worldWeaponObject.WorldRotation = transform.Rotation * rotationOffset;
        _worldWeaponObject.LocalScale = Vector3.One * scale;
    }

    private bool TryGetWeaponVisualBaseTransform(out Transform transform)
    {
        transform = default;

        if (Renderer.IsValid())
        {
            foreach (var boneName in WeaponVisualBoneNames)
            {
                if (Renderer.TryGetBoneTransform(boneName, out transform))
                    return true;
            }
        }

        transform = new Transform(WorldPosition + Vector3.Up * 48f + WorldRotation.Forward * 12f, WorldRotation, 1f);
        return true;
    }

    private void DestroyWorldWeaponVisual()
    {
        if (_worldWeaponObject.IsValid())
            _worldWeaponObject.Destroy();

        _worldWeaponObject = null;
        _worldWeaponItemId = null;
        _worldWeaponUsesAuthoredPrefab = false;
    }

    protected override void OnStart()
	{
        GameObject.Tags.Add( "player" );
        MakeLocalInstance();
        RegisterItemUseHandlers();
        RegisterJobInventoryEvents();
        // HostInitSave/HostInitInventorySave НЕ зовём здесь:
        // на хосте OnStart для клиентского Player может выполняться до того,
        // как у GameObject уже проставлен Network.Owner, и тогда инициализация
        // молча обрывается. Сейв инициализируется по запросу клиента из
        // NetworkInit (см. RpcRequestPlayerSaveInit / RpcRequestPlayerInventoryInit).
        NetworkInit();
    }

    protected override void OnFixedUpdate()
    {
        ApplyQueuedElevatorCarryDelta();
        HostUpdateDeathRespawn();
        UpdateArrestEffects();
        CheckUseHotbarSlots();
        TryUndoLastOwnedProp();
    }

    protected override void OnUpdate()
    {
        TryApplyOwnerClothing();
        UpdateWorldWeaponVisual();
        DrawPhysgunBeam();
    }

    protected override void OnDestroy()
    {
        UnhookInventoryEvents();

        RestoreDeathState();
        DestroyWorldWeaponVisual();
        DestroyLocalInstance();
    }

    public void SetPhysgunBeam(bool active, Vector3 start = default, Vector3 end = default, Vector3 bend = default)
    {
        if (IsProxy)
            return;

        PhysgunBeamActive = active;
        PhysgunBeamStart = start;
        PhysgunBeamEnd = end;
        PhysgunBeamBend = bend;
    }

    private void DrawPhysgunBeam()
    {
        if (!PhysgunBeamActive)
            return;

        var start = PhysgunBeamStart;
        var end = PhysgunBeamEnd;
        if ((end - start).LengthSquared <= 1f)
            return;

        var bend = PhysgunBeamBend;
        var p1 = LerpVector(start, end, 0.35f) + bend;
        var p2 = LerpVector(start, end, 0.70f) + bend;

        var previousColor = Gizmo.Draw.Color;
        var previousThickness = Gizmo.Draw.LineThickness;

        DrawPhysgunBeamCurve(start, p1, p2, end, new Color(0.20f, 0.85f, 1f, 0.22f), 7f);
        DrawPhysgunBeamFlicker(start, p1, p2, end);
        DrawPhysgunBeamCurve(start, p1, p2, end, new Color(0.45f, 0.95f, 1f, 0.95f), 2.5f);

        Gizmo.Draw.Color = previousColor;
        Gizmo.Draw.LineThickness = previousThickness;
    }

    private static void DrawPhysgunBeamCurve(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Color color, float thickness)
    {
        const int segments = 18;

        Gizmo.Draw.Color = color;
        Gizmo.Draw.LineThickness = thickness;

        var previous = p0;
        for (var i = 1; i <= segments; i++)
        {
            var t = i / (float)segments;
            var point = CubicBezier(p0, p1, p2, p3, t);
            Gizmo.Draw.Line(previous, point);
            previous = point;
        }
    }

    private static void DrawPhysgunBeamFlicker(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
    {
        const int sparks = 7;

        var length = Vector3.DistanceBetween(p0, p3);
        var amplitude = MathF.Min(14f, MathF.Max(3f, length * 0.018f));
        var timeSeed = MathF.Floor(Time.Now * 18f);

        Gizmo.Draw.Color = new Color(0.78f, 1f, 1f, 0.72f);
        Gizmo.Draw.LineThickness = 1.35f;

        for (var i = 0; i < sparks; i++)
        {
            var seed = i * 19.19f + timeSeed * 3.71f;
            if (Hash01(seed) < 0.32f)
                continue;

            var t0 = (i + 1f) / (sparks + 2f);
            var t1 = MathF.Min(0.98f, t0 + 0.035f + Hash01(seed + 4.4f) * 0.04f);
            var a = CubicBezier(p0, p1, p2, p3, t0);
            var c = CubicBezier(p0, p1, p2, p3, t1);
            var dir = c - a;
            if (dir.LengthSquared <= 0.001f)
                continue;

            dir = dir.Normal;
            var side = CrossVector(dir, Vector3.Up);
            if (side.LengthSquared <= 0.001f)
                side = CrossVector(dir, Vector3.Right);

            side = side.LengthSquared > 0.001f ? side.Normal : Vector3.Right;
            var up = CrossVector(side, dir);
            up = up.LengthSquared > 0.001f ? up.Normal : Vector3.Up;

            var offset = (side * (Hash01(seed + 8.8f) - 0.5f) + up * (Hash01(seed + 12.2f) - 0.5f)) * amplitude;
            var b = LerpVector(a, c, 0.5f) + offset;

            Gizmo.Draw.Line(a, b);
            Gizmo.Draw.Line(b, c);
        }
    }

    private static Vector3 CubicBezier(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        var u = 1f - t;
        return p0 * (u * u * u)
            + p1 * (3f * u * u * t)
            + p2 * (3f * u * t * t)
            + p3 * (t * t * t);
    }

    private static Vector3 LerpVector(Vector3 a, Vector3 b, float t)
    {
        return a + (b - a) * t;
    }

    private static Vector3 CrossVector(Vector3 a, Vector3 b)
    {
        return new Vector3(
            a.y * b.z - a.z * b.y,
            a.z * b.x - a.x * b.z,
            a.x * b.y - a.y * b.x
        );
    }

    private static float Hash01(float value)
    {
        var s = MathF.Sin(value * 12.9898f) * 43758.5453f;
        return s - MathF.Floor(s);
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

    private Player FindCallerPlayer()
    {
        var caller = Rpc.Caller;
        return caller is null ? null : FindPlayerBySteamId(caller.SteamId.Value);
    }

    public static Player FindPlayerBySteamId( long steamId )
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

    public void RegisterSpawnedProp( PropCustom prop )
    {
        if ( !prop.IsValid() )
            return;

        _ownedPropSpawnStack.RemoveAll( x => !x.IsValid() || x == prop );
        _ownedPropSpawnStack.Add( prop );
    }

    private void TryUndoLastOwnedProp()
    {
        if ( IsProxy )
            return;
        if ( !Input.Pressed( "Undo" ) )
            return;

        var prop = GetLastOwnedProp();
        if ( !prop.IsValid() )
        {
            Notification.Error( "У тебя нет заспавненных пропов.", 2.5f );
            return;
        }

        var propObject = prop.GameObject;
        var propName = !propObject.IsValid() || string.IsNullOrWhiteSpace( propObject.Name ) ? "Prop" : propObject.Name;
        propObject?.Destroy();
        Notification.Info( $"Удален проп: {propName}", 2.8f );
    }

    private PropCustom GetLastOwnedProp()
    {
        _ownedPropSpawnStack.RemoveAll( x => !x.IsValid() || !x.GameObject.IsValid() );

        for ( int i = _ownedPropSpawnStack.Count - 1; i >= 0; i-- )
        {
            var prop = _ownedPropSpawnStack[i];
            if ( !prop.IsValid() || !prop.GameObject.IsValid() )
                continue;
            if ( prop.PlayerOwner != this )
                continue;
            if ( prop.GameObject.Network.Owner != Connection.Local )
                continue;

            _ownedPropSpawnStack.RemoveAt( i );
            return prop;
        }

        return null;
    }

    private static string GetConnectionName( Connection connection )
    {
        return string.IsNullOrWhiteSpace( connection?.DisplayName ) ? "игроку" : connection.DisplayName;
    }

    private static void NotifyInventoryResult(Connection connection, string message, bool success)
    {
        if (connection is null)
            return;

        using (Rpc.FilterInclude(c => c.SteamId.Value == connection.SteamId.Value))
        {
            RpcReceiveInventoryResult(message, success);
        }
    }

    [Rpc.Broadcast]
    private static void RpcReceiveInventoryResult(string message, bool success)
    {
        if (success)
            Notification.Info(message, 3.5f);
        else
            Notification.Error(message, 3.5f);
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
                HostSetEquippedWeaponItemId(null);
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
        HostSetEquippedWeaponItemId(null);

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
            ClearQueuedElevatorCarryDelta();
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
