using Ambi.Storage;
using Ambi.Utils;
using Minimal.ItemUseHandlers;
using Sandbox;
using Sandbox.Citizen;
using Sandbox.Rendering;
using Sandbox.Utility;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Text.Json.Serialization;

public sealed class Player : Component, ICustomDamagable, PlayerController.IEvents
{
    public static Player Local { get; private set; }
    private static readonly SoundEvent DefaultPhysgunBeamStartSound = new("weapons/physgun/sounds/physgun.shoot.start.sound");
    private static readonly SoundEvent DefaultPhysgunBeamActiveLoopSound = new("weapons/physgun/sounds/physgun.active.loop.sound");
    private static readonly SoundEvent DefaultPhysgunIdleSound = new("weapons/physgun/sounds/physgun.idle.sound");

    [Property] public PlayerController Controller { get; private set; }
    [Property] public SkinnedModelRenderer Renderer { get; private set; }
    [Property] public Dresser Dresser { get; private set; }
    [Property] public PlayerWorldHud WorldHud { get; private set; }
    [Property, Sync(SyncFlags.FromHost)] public PlayerJob Job { get; private set; }
    [Property] public GameObject ItemDropPrefab { get; private set; }
    [Property] public GameObject MoneyDropPrefab { get; private set; }
    [Property, Category("Physgun Beam")] public GameObject PhysgunBeamPrefab { get; set; }
    [Property, Category("Physgun Beam")] public Material PhysgunBeamMaterial { get; set; }
    [Property, Category("Physgun Beam")] public GameObject PhysgunBeamEndPointEffectPrefab { get; set; }
    [Property, Category("Physgun Beam")] public GameObject PhysgunBeamGrabEffectPrefab { get; set; }
    [Property, Category("Sounds")] public SoundEvent HitSound { get; set; }
    [Property, Category("Weapons")] public GameObject HoldRBone { get; set; }

    /// <summary>Maximum number of doors this player can own at once.</summary>
    [Property] public int MaxDoors { get; set; } = 8;

    /// <summary>Maximum number of spawned props this player can own at once.</summary>
    [Property] public int MaxProps { get; set; } = 20;

    [Sync(SyncFlags.FromHost)] public bool IsGod { get; set; } = false;
    [Sync(SyncFlags.FromHost)] public bool IsSafezone { get; set; } = false;
    [Sync(SyncFlags.FromHost)] public bool IsCasino { get; set; } = false;
    [Sync(SyncFlags.FromHost)] public float Health { get; set; } = 100f;
    [Sync(SyncFlags.FromHost)] public float MaxHealth { get; set; } = 100f;
    [Sync(SyncFlags.FromHost)] public float Armor { get; set; } = 0f; //todo make
    [Sync(SyncFlags.FromHost)] public float MaxArmor { get; set; } = 100f; //todo make
    [Sync(SyncFlags.FromHost)] public int Level { get; set; } = 1; //todo make
    [Sync(SyncFlags.FromHost)] public int Exp { get; set; } = 0; //todo make
    [Sync(SyncFlags.FromHost)] public int MaxExp { get; set; } = 0; //todo make
    [Sync(SyncFlags.FromHost)] public bool IsDead { get; private set; }
    [Sync(SyncFlags.FromHost)] public string DeathMessage { get; private set; } = "";
    [Sync(SyncFlags.FromHost)] public string JobWorkshopItemsSerialized { get; private set; } = "";
    [Sync(SyncFlags.FromHost)] public int JobWorkshopClothingRevision { get; private set; }

    // TimeUntil должен жить в backing field: с auto-property обратный отсчёт может ломаться.
    private TimeUntil _deathTimeUntilRespawn;
    [Sync(SyncFlags.FromHost)]
    public TimeUntil DeathTimeUntilRespawn
    {
        get => _deathTimeUntilRespawn;
        private set => _deathTimeUntilRespawn = value;
    }

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

    private int _moneyAtm;
    [Sync(SyncFlags.FromHost)]
    public int MoneyAtm
    {
        get => _moneyAtm;
        set
        {
            if (_moneyAtm == value) return;
            _moneyAtm = value;
            if (Networking.IsHost && _saveInitialized)
                SavePlayerData();
        }
    }

    [Sync(SyncFlags.FromHost)] public int AdminRank { get; set; } = 0;

    /// <summary>Арестован ли игрок. Меняется только хостом.</summary>
    [Sync(SyncFlags.FromHost)] public bool IsArrested { get; set; }

    /// <summary>Время до автоматического освобождения. Считается на клиенте, по истечении клиент шлёт RPC хосту.</summary>
    private TimeUntil _arrestTimeUntilRelease;
    [Sync(SyncFlags.FromHost)]
    public TimeUntil ArrestTimeUntilRelease
    {
        get => _arrestTimeUntilRelease;
        set => _arrestTimeUntilRelease = value;
    }

    private bool _arrestSpeedApplied;
    private float _origWalkSpeed;
    private float _origRunSpeed;

    // ===== Lockpick cooldown =====
    /// <summary>Время до окончания кулдауна на взлом дверей. Хост авторитетен.</summary>
    private TimeUntil _lockpickCooldown;
    [Sync(SyncFlags.FromHost)]
    public TimeUntil LockpickCooldown
    {
        get => _lockpickCooldown;
        set => _lockpickCooldown = value;
    }

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
    private long _ownerClothingSteamId;
    private int _ownerClothingApplyPasses;
    private string _observedJobWorkshopItemsSerialized = "";
    private int _observedJobWorkshopClothingRevision = -1;
    private bool _jobWorkshopItemsSyncPending = true;
    private TimeUntil _nextOwnerClothingApplyAttempt = 0f;
    private TimeUntil _nextLocalUiEnsure = 0f;
    private static bool _jobInventoryEventsRegistered;
    private const float MovementSafePitchClamp = 89f;
    private const int OwnerClothingMaxApplyPasses = 3;

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

    [Sync(SyncFlags.FromHost)] public bool PhysgunBeamActive { get; private set; }
    [Sync(SyncFlags.FromHost)] public Vector3 PhysgunBeamStart { get; private set; }
    [Sync(SyncFlags.FromHost)] public Vector3 PhysgunBeamEnd { get; private set; }
    [Sync(SyncFlags.FromHost)] public Vector3 PhysgunBeamEndNormal { get; private set; } = Vector3.Up;
    [Sync(SyncFlags.FromHost)] public Vector3 PhysgunBeamBend { get; private set; }
    [Sync(SyncFlags.FromHost)] public bool PhysgunBeamGrabbed { get; private set; }

    private static bool _itemUseHandlersRegistered;
    private GameObject _worldWeaponObject;
    private string _worldWeaponItemId;
    private string _worldWeaponFailedItemId;
    private bool _worldWeaponAttachedToBone;
    private Vector3 _worldWeaponPrefabOffset;
    private Rotation _worldWeaponPrefabRotation;
    private float _worldWeaponPrefabScale;
    private int _worldWeaponPrefabHandedness;
    private bool _weaponVisualRendererPrepared;
    private GameObject _physgunBeamObject;
    private LineRenderer _physgunBeamRenderer;
    private Vector3.SpringDamped _physgunBeamMiddleSpring = new Vector3.SpringDamped(0, 0);
    private float _physgunBeamPreviousDistance;
    private GameObject _physgunBeamEndPointEffect;
    private GameObject _physgunBeamGrabEffect;
    private SoundHandle _physgunBeamActiveLoopSound;
    private SoundHandle _physgunWorldIdleSound;
    private bool _physgunBeamSoundActive;
    private bool _localPhysgunBeamOverrideSet;
    private bool _localPhysgunBeamOverrideActive;
    private Vector3 _localPhysgunBeamOverrideStart;
    private Vector3 _localPhysgunBeamOverrideEnd;
    private Vector3 _localPhysgunBeamOverrideEndNormal = Vector3.Up;
    private Vector3 _localPhysgunBeamOverrideBend;
    private bool _localPhysgunBeamOverrideGrabbed;


    public sealed class PlayerSaveData
    {
        [JsonPropertyName( "steamId" )] public long SteamId { get; set; }
        [JsonPropertyName( "money" )] public int Money { get; set; }
        [JsonPropertyName( "moneyAtm" )] public int MoneyAtm { get; set; }
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

        var hasSpawnTransform = TryGetSpawnTransform(out var spawnPosition, out var spawnRotation);
        if (!hasSpawnTransform)
            Log.Warning($"[Player] Respawning {GameObject.Network.Owner?.DisplayName ?? GameObject.Name} without a valid spawn point.");

        if (!IsProxy)
        {
            SpawnInternal(spawnPosition, spawnRotation);
            RpcClearDeathRagdoll();
            HostSetLifePresentationEnabled(true);
            return;
        }

        Health = MaxHealth;
        IsDead = false;
        DeathTimeUntilRespawn = 0f;
        DeathMessage = "";
        ResetFallDamageGrace();
        WorldHud?.WorldHudRefresh();
        Job?.NotifySpawned();

        RpcOwnerSpawn(spawnPosition, spawnRotation);
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
        RequestHostLifePresentationEnabled(true);
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
        var hasSpawnTransform = TryGetSpawnTransform(out var spawnPosition, out var spawnRotation);
        if (!hasSpawnTransform)
            Log.Warning($"[Player] Spawning {GameObject.Network.Owner?.DisplayName ?? GameObject.Name} without a valid spawn point.");

        SpawnInternal(spawnPosition, spawnRotation);
    }

    private void SpawnInternal(Vector3 spawnPosition, Rotation spawnRotation)
    {
        Health = MaxHealth;
        IsDead = false;
        DeathTimeUntilRespawn = 0f;
        DeathMessage = "";
        RestoreDeathState();
        RequestHostLifePresentationEnabled(true);
        ResetFallDamageGrace();
        WorldHud?.WorldHudRefresh();
        ClearQueuedElevatorCarryDelta();
        WorldPosition = spawnPosition;

        if (Controller.IsValid())
            Controller.EyeAngles = spawnRotation;

        Job?.NotifySpawned();
    }

    private bool TryGetSpawnTransform(out Vector3 position, out Rotation rotation)
    {
        var spawnPoint = SpawnManager.Instance?.GetRandomPlayerSpawn();
        if (spawnPoint.IsValid())
        {
            position = spawnPoint.WorldPosition;
            rotation = spawnPoint.WorldRotation;
            return true;
        }

        position = WorldPosition;
        rotation = WorldRotation;
        return false;
    }

    public void OnDamage(in DamageInfo dmgInfo)
    {
        // Server (host) authority: урон применяет ТОЛЬКО хост; клиент только просит.
        if (IsArrested) return;
        if (IsSafezone) return;

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

    [Rpc.Broadcast]
    private void RpcSetHoldTypeHandedness(SkinnedModelRenderer renderer, int handedness)
    {
        renderer?.Set("holdtype_handedness", handedness);
    }

    [Rpc.Broadcast]
    public void RpcSetPlayerReload(bool isReloading)
    {
        Renderer?.Set("b_reload", isReloading);
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
        if (IsSafezone) return;
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
        DeathMessage = string.IsNullOrWhiteSpace(deathMessage) ? GameLocalization.Phrase( "ui.hud.default_death_message", "You died." ) : deathMessage;
        HostSetEquippedWeaponItemId(null);
        WorldHud?.WorldHudRefresh();

        RpcOwnerDied(DeathMessage, (float)DeathTimeUntilRespawn);
        RpcCreateDeathRagdoll();
        HostSetLifePresentationEnabled(false);
    }

    public void HostKill(string deathMessage = null)
    {
        if (!Networking.IsHost) return;

        HostDie(deathMessage);
    }

    [Rpc.Owner]
    private void RpcOwnerDied(string deathMessage, float respawnDelay)
    {
        ClearQueuedElevatorCarryDelta();
        IsDead = true;
        Health = 0f;
        DeathTimeUntilRespawn = MathF.Max(0.1f, respawnDelay);
        DeathMessage = string.IsNullOrWhiteSpace(deathMessage) ? GameLocalization.Phrase( "ui.hud.default_death_message", "You died." ) : deathMessage;

        if (CurrentWeapon.IsValid())
            CurrentWeapon.GameObject.Enabled = false;

        ApplyDeathControls();
        RequestHostLifePresentationEnabled(false);
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

    private void RequestHostLifePresentationEnabled(bool enabled)
    {
        if (IsProxy)
            return;

        if (Networking.IsHost)
        {
            HostSetLifePresentationEnabled(enabled);
            return;
        }

        RpcRequestHostLifePresentationEnabled(enabled);
    }

    [Rpc.Host]
    private void RpcRequestHostLifePresentationEnabled(bool enabled)
    {
        if (!Networking.IsHost)
            return;

        var caller = Rpc.Caller;
        if (caller is null)
            return;

        if (GameObject.Network.Owner != caller)
            return;

        HostSetLifePresentationEnabled(enabled);
    }

    private void HostSetLifePresentationEnabled(bool enabled)
    {
        if (!Networking.IsHost)
            return;

        ApplyLifePresentationEnabled(enabled);
        RpcApplyLifePresentationEnabled(enabled);
    }

    [Rpc.Broadcast]
    private void RpcApplyLifePresentationEnabled(bool enabled)
    {
        if (!Networking.IsHost && Rpc.Caller is not null && !Rpc.Caller.IsHost)
            return;

        ApplyLifePresentationEnabled(enabled);
    }

    private void ApplyLifePresentationEnabled(bool enabled)
    {
        if (Controller.IsValid())
        {
            Controller.Enabled = enabled;

            if (!enabled)
            {
                Controller.WishVelocity = Vector3.Zero;

                if (Controller.Body.IsValid())
                    Controller.Body.Velocity = Vector3.Zero;
            }
        }

        if (Renderer.IsValid() && Renderer.GameObject.IsValid())
            Renderer.GameObject.Enabled = enabled;
    }

    private void HostUpdateDeathRespawn()
    {
        if (!Networking.IsHost) return;
        if (!IsDead) return;
        if ((float)_deathTimeUntilRespawn > 0f) return;

        HostTriggerRespawn();
    }

    private static string BuildDeathMessage(GameObject attacker, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(fallback))
            return fallback;

        var attackerName = GetAttackerDisplayName(attacker);
        if (!string.IsNullOrWhiteSpace(attackerName))
            return GameLocalization.Format( "ui.hud.killed_by", "You were killed by \"{0}\"", attackerName );

        return GameLocalization.Phrase( "ui.hud.default_death_message", "You died." );
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
        HostApplyDamage(damage, null, GameLocalization.Phrase( "ui.hud.fall_death", "You died from a fall." ) );
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
            RpcSetHoldTypeHandedness(Renderer, 0);
            RpcSetPlayerReload(false);

            return;
        }

        CurrentWeapon = wep;
        CurrentWeapon.GameObject.Enabled = true;
        RpcSetHoldType(Renderer, (int)CurrentWeapon.HoldType);
        RpcSetHoldTypeHandedness(Renderer, (int)CurrentWeapon.HoldTypeHandedness);
        RpcSetPlayerReload(false);
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
        if ( isWeapon && IsProxy )
        {
            // On a dedicated host the remote player's viewmodel weapons live only on
            // the owning client. Approve the equip server-side, then let the owner
            // run the item handler locally via RpcOwnerUseInventorySlotApproved.
            successful = true;
        }
        else
        {
            _deferInventorySync = true;
            try
            {
                successful = Inventory.TryUseItem(slot, this);
            }
            finally
            {
                _deferInventorySync = false;
            }
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
            NotifyInventoryResult(GameObject.Network.Owner, GameLocalization.Phrase( "notify.inventory.cannot_drop", "This item cannot be dropped." ), false);
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

        NotifyInventoryResult(GameObject.Network.Owner, GameLocalization.Format( "notify.inventory.dropped", "You dropped: {0}", GameLocalization.ItemHeader( item.Definition ) ), true);
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

    public bool HostAddJobWorkshopItem(string packageId)
    {
        if (!Networking.IsHost) return false;
        if (string.IsNullOrWhiteSpace(packageId)) return false;

        var items = DeserializeJobWorkshopItems(JobWorkshopItemsSerialized);
        if (items.Contains(packageId)) return false;

        items.Add(packageId);
        HostSetJobWorkshopItems(items);
        return true;
    }

    public bool HostRemoveJobWorkshopItem(string packageId)
    {
        if (!Networking.IsHost) return false;
        if (string.IsNullOrWhiteSpace(packageId)) return false;

        var items = DeserializeJobWorkshopItems(JobWorkshopItemsSerialized);
        if (!items.Remove(packageId)) return false;

        HostSetJobWorkshopItems(items);
        return true;
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
            : GameLocalization.ItemHeader( droppedItem.ItemDefinition );

        var taken = droppedItem.TryPickup(Inventory);
        if (taken <= 0)
        {
            SendInventorySnapshotToOwner();
            return 0;
        }

        NotifyInventoryResult(GameObject.Network.Owner, GameLocalization.Format( "notify.inventory.picked_up", "Picked up {0} x{1}", itemName, taken ), true);
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
        ItemUseRegistry.Register("pickaxe", new WepPickaxeUseHandler());
        ItemUseRegistry.Register("ammo_usp", new AmmoUseHandler(AmmoWeaponType.Usp));
        ItemUseRegistry.Register("ammo_mp5", new AmmoUseHandler(AmmoWeaponType.Mp5));
        ItemUseRegistry.Register("ammo_m4a1", new AmmoUseHandler(AmmoWeaponType.M4A1));
        ItemUseRegistry.Register("burger", new WepBurgerUseHandler());

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
#if SERVER
        // Dedicated servers should not build cosmetic clothing. Let every client
        // apply visuals from the network owner connection instead.
        return;
#else
        RefreshJobWorkshopClothingApplyState();

        var ownerSteamId = GetOwnerSteamId();
        if (ownerSteamId <= 0L)
        {
            if (_ownerClothingSteamId != 0L)
                ResetOwnerClothingApplyState(0L);

            ScheduleOwnerClothingRetry(0.25f);
            return;
        }

        if (_ownerClothingSteamId != ownerSteamId)
            ResetOwnerClothingApplyState(ownerSteamId);

        if (_ownerClothingApplyInProgress)
            return;
        if (_ownerClothingApplied && _ownerClothingApplyPasses >= OwnerClothingMaxApplyPasses)
            return;
        if (!_nextOwnerClothingApplyAttempt)
            return;
        if (!Dresser.IsValid())
        {
            ScheduleOwnerClothingRetry(0.5f);
            return;
        }
        if (!Renderer.IsValid())
        {
            ScheduleOwnerClothingRetry(0.5f);
            return;
        }

        _ = ApplyOwnerClothingAsync(ownerSteamId);
#endif
    }

    private async Task ApplyOwnerClothingAsync(long ownerSteamId)
    {
        _ownerClothingApplyInProgress = true;
        _ownerClothingApplyPasses++;

        try
        {
            Dresser.Source = Dresser.ClothingSource.Manual;
            var a = ClothingContainer.CreateFromConnection(GameObject.Network.Owner, false); //todo Изменил под будущую систему Job Skins, но в dresser жуткий пиздец, надо доделывать
            Dresser.Clothing = a.Clothing;
            await Dresser.Apply();

            var currentOwnerSteamId = GameObject.IsValid() ? GetOwnerSteamId() : 0L;
            if (currentOwnerSteamId != ownerSteamId)
            {
                ResetOwnerClothingApplyState(currentOwnerSteamId);
                return;
            }

            _ownerClothingApplied = true;

            if (_ownerClothingApplyPasses < OwnerClothingMaxApplyPasses)
                ScheduleOwnerClothingRetry(_ownerClothingApplyPasses == 1 ? 1f : 3f);
        }
        catch (Exception ex)
        {
            ScheduleOwnerClothingRetry(2f);
            var ownerName = GameObject.IsValid() ? GameObject.Network.Owner?.DisplayName ?? "unknown" : "unknown";
            Log.Warning($"[Player] Failed to apply owner clothing for {ownerName}: {ex.Message}");
        }
        finally
        {
            _ownerClothingApplyInProgress = false;
        }
    }

    private void ResetOwnerClothingApplyState(long ownerSteamId)
    {
        _ownerClothingSteamId = ownerSteamId;
        _ownerClothingApplied = false;
        _ownerClothingApplyPasses = 0;
        _ownerClothingApplyInProgress = false;
        _nextOwnerClothingApplyAttempt = 0f;
    }

    private void ScheduleOwnerClothingRetry(float delaySeconds)
    {
        _nextOwnerClothingApplyAttempt = MathF.Max(0.05f, delaySeconds);
    }

    private void RefreshJobWorkshopClothingApplyState()
    {
        var serialized = JobWorkshopItemsSerialized ?? "";
        var revisionChanged = _observedJobWorkshopClothingRevision != JobWorkshopClothingRevision;
        var serializedChanged = !string.Equals(_observedJobWorkshopItemsSerialized, serialized, StringComparison.Ordinal);

        if (!revisionChanged && !serializedChanged && !_jobWorkshopItemsSyncPending)
            return;

        if (!Dresser.IsValid())
        {
            _jobWorkshopItemsSyncPending = true;
            return;
        }

        SyncJobWorkshopItemsToDresser(serialized);
        _observedJobWorkshopItemsSerialized = serialized;
        _observedJobWorkshopClothingRevision = JobWorkshopClothingRevision;
        _jobWorkshopItemsSyncPending = false;
        ResetOwnerClothingApplyState(GetOwnerSteamId());
        ScheduleOwnerClothingRetry(0.05f);
    }

    private void HostRefreshJobWorkshopClothing()
    {
        if (!Networking.IsHost)
            return;

        JobWorkshopClothingRevision++;
    }

    private void HostSetJobWorkshopItems(List<string> items)
    {
        if (!Networking.IsHost)
            return;

        JobWorkshopItemsSerialized = SerializeJobWorkshopItems(items);

        if (Dresser.IsValid())
            SyncJobWorkshopItemsToDresser(JobWorkshopItemsSerialized);

        HostRefreshJobWorkshopClothing();
    }

    private void SyncJobWorkshopItemsToDresser(string serialized)
    {
        if (!Dresser.IsValid())
            return;

        var desiredItems = DeserializeJobWorkshopItems(serialized);
        var currentItems = new List<string>(Dresser.WorkshopItems);

        foreach (var currentItem in currentItems)
        {
            if (!desiredItems.Contains(currentItem))
                Dresser.WorkshopItems.Remove(currentItem);
        }

        foreach (var desiredItem in desiredItems)
        {
            if (!Dresser.WorkshopItems.Contains(desiredItem))
                Dresser.WorkshopItems.Add(desiredItem);
        }
    }

    private static List<string> DeserializeJobWorkshopItems(string serialized)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(serialized))
            return result;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var parts = serialized.Split(';', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var packageId = part.Trim();
            if (string.IsNullOrWhiteSpace(packageId))
                continue;
            if (!seen.Add(packageId))
                continue;

            result.Add(packageId);
        }

        return result;
    }

    private static string SerializeJobWorkshopItems(List<string> items)
    {
        if (items is null || items.Count == 0)
            return "";

        var uniqueItems = new List<string>(items.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item))
                continue;

            var packageId = item.Trim();
            if (!seen.Add(packageId))
                continue;

            uniqueItems.Add(packageId);
        }

        return string.Join(";", uniqueItems);
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

        PlayerJob.OnJobChanged += HandleJobChangedRemoveJobItems;
        PlayerJob.OnJobDemote += HandleJobDemoteRemoveJobItems;
        _jobInventoryEventsRegistered = true;
    }

    private static void HandleJobChangedRemoveJobItems(Player player, JobDefinition _)
    {
        player?.HostRemoveJobItems();
    }

    private static void HandleJobDemoteRemoveJobItems(Player player)
    {
        player?.HostRemoveJobItems();
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
                Money = DefaultStartingMoney,
                MoneyAtm = 0
            };
        }

        // Сначала разрешаем персист, чтобы сеттер мог сохранять при изменениях.
        _saveInitialized = true;

        // Применяем через property-сеттер, а не в backing field напрямую.
        // [Sync(FromHost)] трекает изменение через property; запись в _money
        // напрямую не уведомляла клиентов, из-за чего Hud не показывал
        // загруженное значение после инициализации.
        Money = data.Money;
        MoneyAtm = data.MoneyAtm;

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
                Money = _money,
                MoneyAtm = _moneyAtm
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
        if (IsProxy) return;

        Local = this;
        EnsureLocalRuntimeUi();
    }

    private void EnsureLocalRuntimeUi()
    {
        if (IsProxy) return;
        if (!_nextLocalUiEnsure) return;

        _nextLocalUiEnsure = 1f;
        Crosshair.EnsureExists();
    }

    private void DestroyLocalInstance()
    {
        if (Local == this)
            Local = null;
    }

    private void UpdateWorldWeaponVisual()
    {
        EnsureWeaponVisualRendererReady();
        UpdateProxyWeaponHoldType();

        var itemId = GetWorldWeaponVisualItemId();
        if (IsArrested || string.IsNullOrWhiteSpace(itemId))
        {
            DestroyWorldWeaponVisual();
            return;
        }

        var wmPrefab = GetWorldModelPrefabForItem(itemId);
        if (!wmPrefab.IsValid())
        {
            DestroyWorldWeaponVisual();
            return;
        }

        if (_worldWeaponFailedItemId == itemId)
        {
            if (!string.Equals(_worldWeaponItemId, itemId, StringComparison.OrdinalIgnoreCase))
                DestroyWorldWeaponVisual();
            return;
        }

        if (!EnsureWorldWeaponVisual(itemId, wmPrefab))
            return;

        UpdateWorldWeaponTransform();
        ConfigureWorldWeaponVisualRendering();
    }

    private string GetWorldWeaponVisualItemId()
    {
        if (IsProxy)
            return EquippedWeaponItemId;

        return !string.IsNullOrWhiteSpace(CurrentWeaponItemId)
            ? CurrentWeaponItemId
            : EquippedWeaponItemId;
    }

    private bool EnsureWorldWeaponVisual(string itemId, GameObject wmPrefab)
    {
        if (_worldWeaponObject.IsValid()
            && string.Equals(_worldWeaponItemId, itemId, StringComparison.OrdinalIgnoreCase))
            return true;

        DestroyWorldWeaponVisual();

        var boneParent = TryGetWeaponVisualParentObject();
        if (!boneParent.IsValid())
            return false;

        _worldWeaponObject = wmPrefab.Clone(new CloneConfig
        {
            Parent = boneParent,
            StartEnabled = true,
            Transform = global::Transform.Zero
        });
        if (!_worldWeaponObject.IsValid())
        {
            _worldWeaponFailedItemId = itemId;
            Log.Warning($"[Player] Failed to clone world weapon visual for '{itemId}'");
            return false;
        }

        _worldWeaponObject.Name = $"world_weapon_{itemId}";
        _worldWeaponObject.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
        _worldWeaponAttachedToBone = true;

        _worldWeaponPrefabOffset = Vector3.Zero;
        _worldWeaponPrefabRotation = Rotation.Identity;
        _worldWeaponPrefabScale = 1f;
        _worldWeaponPrefabHandedness = 0;

        if (_worldWeaponObject.Components.TryGet<WeaponWorldModel>(out var wmComp))
        {
            _worldWeaponPrefabOffset = wmComp.PositionOffset;
            _worldWeaponPrefabRotation = wmComp.RotationOffset;
            _worldWeaponPrefabScale = wmComp.Scale > 0f ? wmComp.Scale : 1f;
        }

        var wepInst = GetWeaponInstanceForItem(itemId);
        if (wepInst.IsValid())
            _worldWeaponPrefabHandedness = (int)wepInst.HoldTypeHandedness;

        ApplyWorldWeaponLocalTransform();

        _worldWeaponItemId = itemId;
        _worldWeaponFailedItemId = null;
        ConfigureWorldWeaponVisualRendering();
        Log.Info($"[Player] World weapon visual '{itemId}' spawned from WM prefab");
        return true;
    }

    private void ConfigureWorldWeaponVisualRendering()
    {
        if (!_worldWeaponObject.IsValid())
            return;

        var renderType = IsProxy
            ? ModelRenderer.ShadowRenderType.On
            : ModelRenderer.ShadowRenderType.ShadowsOnly;

        foreach (var renderer in _worldWeaponObject.Components.GetAll<ModelRenderer>(FindMode.EverythingInSelfAndDescendants))
        {
            if (!renderer.IsValid())
                continue;

            renderer.RenderType = renderType;

            if (renderer.SceneObject is not null)
                renderer.SceneObject.Flags.CastShadows = true;

            if (renderer is SkinnedModelRenderer skinnedRenderer && skinnedRenderer.SceneModel is not null)
                skinnedRenderer.SceneModel.Flags.CastShadows = true;
        }
    }

    private static Weapon GetWeaponInstanceForItem(string itemId)
    {
        var manager = WeaponManager.Instance;
        if (!manager.IsValid()) return null;
        return itemId switch
        {
            "usp"      => manager.Usp,
            "mp5"      => manager.Mp5,
            "m4a1"     => manager.M4A1,
            "physgun"  => manager.Physgun,
            "toolgun"  => manager.Toolgun,
            "pickaxe"  => manager.Pickaxe,
            "picklock" => manager.Picklock,
            "handcuff" => manager.Handcuff,
            "keys"     => manager.Keys,
            _          => null
        };
    }

    private static GameObject GetWorldModelPrefabForItem(string itemId)
    {
        var weapon = GetWeaponInstanceForItem(itemId);
        return weapon?.WorldModelPrefab;
    }

    private void UpdateProxyWeaponHoldType()
    {
        if (!IsProxy || !Renderer.IsValid())
            return;

        var holdType = IsArrested || string.IsNullOrWhiteSpace(EquippedWeaponItemId)
            ? CitizenAnimationHelper.HoldTypes.None
            : GetWeaponHoldType(EquippedWeaponItemId);

        var handedness = IsArrested || string.IsNullOrWhiteSpace(EquippedWeaponItemId)
            ? 0
            : _worldWeaponPrefabHandedness;

        Renderer.Set("holdtype", (int)holdType);
        Renderer.Set("holdtype_handedness", handedness);
    }

    private static CitizenAnimationHelper.HoldTypes GetWeaponHoldType(string itemId)
    {
        return itemId switch
        {
            "usp" => CitizenAnimationHelper.HoldTypes.Pistol,
            "toolgun" => CitizenAnimationHelper.HoldTypes.Pistol,
            "mp5" => CitizenAnimationHelper.HoldTypes.Rifle,
            "m4a1" => CitizenAnimationHelper.HoldTypes.Rifle,
            "physgun" => CitizenAnimationHelper.HoldTypes.Physgun,
            "hands" => CitizenAnimationHelper.HoldTypes.Punch,
            "picklock" => CitizenAnimationHelper.HoldTypes.Swing,
            "handcuff" => CitizenAnimationHelper.HoldTypes.Swing,
            _ => CitizenAnimationHelper.HoldTypes.None
        };
    }

    private void UpdateWorldWeaponTransform()
    {
        if (!_worldWeaponObject.IsValid() || !Renderer.IsValid())
            return;

        if (!_worldWeaponAttachedToBone)
        {
            var boneParent = TryGetWeaponVisualParentObject();
            if (boneParent.IsValid())
            {
                _worldWeaponObject.Parent = boneParent;
                _worldWeaponAttachedToBone = true;
                ApplyWorldWeaponLocalTransform();
                return;
            }
        }

        if (_worldWeaponAttachedToBone)
        {
            ApplyWorldWeaponLocalTransform();
            return;
        }

        if (!TryGetWeaponVisualBaseTransform(out var transform))
            return;

        _worldWeaponObject.WorldPosition = transform.Position + transform.Rotation * _worldWeaponPrefabOffset;
        _worldWeaponObject.WorldRotation = transform.Rotation * _worldWeaponPrefabRotation;
        _worldWeaponObject.LocalScale = Vector3.One * _worldWeaponPrefabScale;
    }

    private void ApplyWorldWeaponLocalTransform()
    {
        if (!_worldWeaponObject.IsValid())
            return;

        _worldWeaponObject.LocalPosition = _worldWeaponPrefabOffset;
        _worldWeaponObject.LocalRotation = _worldWeaponPrefabRotation;
        _worldWeaponObject.LocalScale = Vector3.One * _worldWeaponPrefabScale;
    }

    private GameObject TryGetWeaponVisualParentObject()
    {
        EnsureWeaponVisualRendererReady();

        if (!Renderer.IsValid())
            return null;

        return HoldRBone;
    }

    private bool TryGetWeaponVisualBaseTransform(out Transform transform)
    {
        transform = default;

        if (Renderer.IsValid() && Renderer.TryGetBoneTransform("hold_r", out transform))
            return true;

        transform = new Transform(WorldPosition + Vector3.Up * 48f + WorldRotation.Forward * 12f, WorldRotation, 1f);
        return true;
    }

    private void EnsureWeaponVisualRendererReady()
    {
        if (!Renderer.IsValid())
        {
            _weaponVisualRendererPrepared = false;
            return;
        }

        if (_weaponVisualRendererPrepared)
            return;

        Renderer.CreateAttachments = true;
        Renderer.CreateBoneObjects = true;
        _weaponVisualRendererPrepared = true;
    }

    public void PostCameraSetup(CameraComponent camera)
    {
        ApplyLocalFirstPersonShadowRendering();
    }

    private void ApplyLocalFirstPersonShadowRendering()
    {
        if (IsProxy || !Controller.IsValid() || !Renderer.IsValid())
            return;

        var renderType = !Controller.ThirdPerson && IsAlive
            ? ModelRenderer.ShadowRenderType.ShadowsOnly
            : ModelRenderer.ShadowRenderType.On;

        foreach (var renderer in Renderer.GameObject.Components.GetAll<SkinnedModelRenderer>(FindMode.EverythingInSelfAndDescendants))
        {
            if (!renderer.IsValid())
                continue;

            renderer.RenderType = renderType;
        }
    }

    private bool TryGetWorldWeaponChildTransform(string childName, out Transform transform)
    {
        transform = default;

        var child = FindChildRecursive(_worldWeaponObject, childName);
        if (!child.IsValid())
            return false;

        transform = child.WorldTransform;
        return true;
    }

    private static GameObject FindChildRecursive(GameObject root, string name)
    {
        if (!root.IsValid())
            return null;

        if (string.Equals(root.Name, name, StringComparison.OrdinalIgnoreCase))
            return root;

        foreach (var child in root.Children)
        {
            var found = FindChildRecursive(child, name);
            if (found.IsValid())
                return found;
        }

        return null;
    }

    private void DestroyWorldWeaponVisual()
    {
        StopRemotePhysgunIdleSound();

        if (_worldWeaponObject.IsValid())
            _worldWeaponObject.Destroy();

        _worldWeaponObject = null;
        _worldWeaponItemId = null;
        _worldWeaponAttachedToBone = false;
        _worldWeaponPrefabOffset = Vector3.Zero;
        _worldWeaponPrefabRotation = Rotation.Identity;
        _worldWeaponPrefabScale = 1f;
        _worldWeaponPrefabHandedness = 0;
    }

    private void UpdateRemotePhysgunIdleSound()
    {
        if (!IsProxy || !string.Equals(_worldWeaponItemId, "physgun", StringComparison.OrdinalIgnoreCase))
        {
            StopRemotePhysgunIdleSound();
            return;
        }

        var position = _worldWeaponObject.IsValid() ? _worldWeaponObject.WorldPosition : WorldPosition;

        if (_physgunWorldIdleSound is null)
        {
            if (DefaultPhysgunIdleSound.IsValid())
                _physgunWorldIdleSound = Sound.Play(DefaultPhysgunIdleSound, position);

            return;
        }

        _physgunWorldIdleSound.Position = position;
    }

    private void StopRemotePhysgunIdleSound()
    {
        _physgunWorldIdleSound?.Stop();
        _physgunWorldIdleSound = null;
    }

    protected override void OnStart()
	{
        GameObject.Tags.Add( "player" );
        EnsureWeaponVisualRendererReady();
        EnsureMovementSafePitchClamp();
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

    private void EnsureMovementSafePitchClamp()
    {
        if (!Controller.IsValid() || Controller.PitchClamp <= MovementSafePitchClamp)
            return;

        Controller.PitchClamp = MovementSafePitchClamp;
    }

    protected override void OnFixedUpdate()
    {
        ApplyQueuedElevatorCarryDelta();
        HostUpdateDeathRespawn();
        Minimal.Weapons.WeaponPhysgun.HostFixedUpdateForPlayer(this);
        UpdateArrestEffects();
        CheckUseHotbarSlots();
        TryUndoLastOwnedProp();
        TryOpenDoorHudFromInteract();
    }

    /// <summary>
    /// Local client: opens <see cref="DoorHud"/> when Interact (F) is pressed while looking at a door within <see cref="Door.InteractOpenMenuMaxEyeDistance"/>.
    /// </summary>
    public void TryOpenDoorHudFromInteract()
    {
        if (IsProxy) return;
        if (IsArrested) return;
        if (!Input.Pressed("Interact")) return;

        var door = Door.FindLookedAtDoor(this, Door.MenuLookRayLength, Door.InteractOpenMenuMaxEyeDistance);
        if (door is null) return;

        foreach (var doorHud in Scene.GetAllComponents<DoorHud>())
        {
            if (!doorHud.CanOpen) return;
            doorHud.Door = door;
            return;
        }
    }

    protected override void OnUpdate()
    {
        MakeLocalInstance();
        TryApplyOwnerClothing();
        ApplyLocalFirstPersonShadowRendering();
        EnsureWeaponVisualRendererReady();
        UpdateWorldWeaponVisual();
        UpdateRemotePhysgunIdleSound();
        DrawPhysgunBeam();
    }

    protected override void OnDestroy()
    {
        Minimal.Weapons.WeaponPhysgun.HostReleaseForPlayer( this );
        UnhookInventoryEvents();

        RestoreDeathState();
        DestroyPhysgunBeamVisual();
        DestroyWorldWeaponVisual();
        DestroyLocalInstance();
    }

    public void SetPhysgunBeam(bool active, Vector3 start = default, Vector3 end = default, Vector3 bend = default,
        Vector3 endNormal = default, bool grabbed = false)
    {
        if (!Networking.IsHost && IsProxy)
            return;

        PhysgunBeamActive = active;
        PhysgunBeamStart = start;
        PhysgunBeamEnd = end;
        PhysgunBeamBend = bend;
        PhysgunBeamEndNormal = endNormal.LengthSquared > 0.001f ? endNormal.Normal : Vector3.Up;
        PhysgunBeamGrabbed = grabbed;
    }

    public void SetLocalPhysgunBeam(bool active, Vector3 start = default, Vector3 end = default, Vector3 bend = default,
        Vector3 endNormal = default, bool grabbed = false)
    {
        if (IsProxy)
            return;

        _localPhysgunBeamOverrideSet = true;
        _localPhysgunBeamOverrideActive = active;
        _localPhysgunBeamOverrideStart = start;
        _localPhysgunBeamOverrideEnd = end;
        _localPhysgunBeamOverrideBend = bend;
        _localPhysgunBeamOverrideEndNormal = endNormal.LengthSquared > 0.001f ? endNormal.Normal : Vector3.Up;
        _localPhysgunBeamOverrideGrabbed = grabbed;
    }

    public Vector3 GetPhysgunBeamWorldStart(Vector3 fallbackForward)
    {
        var forward = fallbackForward.LengthSquared > 0.001f ? fallbackForward.Normal : WorldRotation.Forward;

        if (TryGetWorldWeaponChildTransform("muzzle", out var muzzleTransform))
            return muzzleTransform.Position + muzzleTransform.Rotation.Forward * 2f;

        if (TryGetWeaponVisualBaseTransform(out var transform))
        {
            var offset = Vector3.Zero;
            if (_worldWeaponObject.IsValid()
                && _worldWeaponObject.Components.TryGet<WeaponWorldModel>(out var wm))
                offset = wm.BeamStartOffset;

            return transform.Position + transform.Rotation * offset + transform.Rotation.Forward * 2f;
        }

        if (Controller.IsValid())
            return Controller.EyeTransform.Position + forward * 18f + Vector3.Down * 6f;

        return WorldPosition + Vector3.Up * 48f + forward * 16f;
    }

    private void DrawPhysgunBeam()
    {
        var useLocalOverride = !IsProxy && _localPhysgunBeamOverrideSet;
        var active = useLocalOverride ? _localPhysgunBeamOverrideActive : PhysgunBeamActive;

        if (!active)
        {
            StopPhysgunBeamSound();
            SetPhysgunBeamVisible(false);
            ClosePhysgunBeamEffects();
            return;
        }

        var start = useLocalOverride ? _localPhysgunBeamOverrideStart : PhysgunBeamStart;
        var end = useLocalOverride ? _localPhysgunBeamOverrideEnd : PhysgunBeamEnd;
        var bend = useLocalOverride ? _localPhysgunBeamOverrideBend : PhysgunBeamBend;
        var endNormal = useLocalOverride ? _localPhysgunBeamOverrideEndNormal : PhysgunBeamEndNormal;
        var grabbed = useLocalOverride ? _localPhysgunBeamOverrideGrabbed : PhysgunBeamGrabbed;

        if (!useLocalOverride
            && IsProxy
            && string.Equals(_worldWeaponItemId, "physgun", StringComparison.OrdinalIgnoreCase)
            && TryGetWorldWeaponChildTransform("muzzle", out var muzzleTransform))
        {
            start = muzzleTransform.Position + muzzleTransform.Rotation.Forward * 2f;
        }

        if ((end - start).LengthSquared <= 1f)
        {
            StopPhysgunBeamSound();
            SetPhysgunBeamVisible(false);
            ClosePhysgunBeamEffects();
            return;
        }

        if (!EnsurePhysgunBeamVisual())
            return;

        UpdatePhysgunBeamSound(start);

        var justEnabled = !_physgunBeamObject.Enabled;

        if (_physgunBeamRenderer.VectorPoints is null || _physgunBeamRenderer.VectorPoints.Count != 4)
            _physgunBeamRenderer.VectorPoints = new List<Vector3> { start, start, end, end };

        var delta = end - start;
        var distance = delta.Length;
        var forward = delta.LengthSquared > 0.001f ? delta.Normal : Vector3.Forward;
        if (endNormal.LengthSquared <= 0.001f)
            endNormal = -forward;
        else
            endNormal = endNormal.Normal;

        var targetMiddle = start + forward * distance * 0.33f + bend * 0.75f;
        targetMiddle += Noise.FbmVector(2, Time.Now * 400.0f, Time.Now * 100.0f);

        if (!justEnabled)
        {
            if (_physgunBeamPreviousDistance > 1f && distance / _physgunBeamPreviousDistance < 0.5f)
                _physgunBeamMiddleSpring = new Vector3.SpringDamped(targetMiddle, targetMiddle, 4f, 0.2f);

            var alongForward = Vector3.Dot(_physgunBeamMiddleSpring.Current - start, forward);
            if (alongForward < 0f)
            {
                var clamped = _physgunBeamMiddleSpring.Current - forward * alongForward;
                _physgunBeamMiddleSpring = new Vector3.SpringDamped(clamped, targetMiddle, 4f, 0.2f);
            }
        }

        _physgunBeamPreviousDistance = distance;

        _physgunBeamRenderer.VectorPoints[0] = start;
        _physgunBeamRenderer.VectorPoints[1] = _physgunBeamMiddleSpring.Current;
        _physgunBeamMiddleSpring.Target = targetMiddle;
        _physgunBeamMiddleSpring.Update(Time.Delta);
        _physgunBeamRenderer.VectorPoints[2] = Vector3.Lerp(end + endNormal * 10f, _physgunBeamRenderer.VectorPoints[1], 0.3f + MathF.Sin(Time.Now * 10f) * 0.2f);
        _physgunBeamRenderer.VectorPoints[3] = end;
        UpdatePhysgunBeamEffects(end, endNormal, grabbed);

        if (justEnabled)
        {
            _physgunBeamObject.Enabled = true;
            _physgunBeamPreviousDistance = distance;
            _physgunBeamRenderer.VectorPoints[1] = targetMiddle;
            _physgunBeamMiddleSpring = new Vector3.SpringDamped(targetMiddle, targetMiddle, 4f, 0.2f);
        }
    }

    private bool EnsurePhysgunBeamVisual()
    {
        if (_physgunBeamRenderer.IsValid())
            return true;

        _physgunBeamObject = PhysgunBeamPrefab.IsValid()
            ? PhysgunBeamPrefab.Clone(Vector3.Zero, Rotation.Identity)
            : new GameObject(true, "Local Physgun Beam");

        if (!_physgunBeamObject.IsValid())
            return false;

        _physgunBeamObject.Name = "Local Physgun Beam";
        _physgunBeamObject.Parent = GameObject;
        _physgunBeamObject.Enabled = false;
        _physgunBeamRenderer = _physgunBeamObject.Components.Get<LineRenderer>(FindMode.EverythingInSelfAndDescendants);

        if (!_physgunBeamRenderer.IsValid())
        {
            _physgunBeamRenderer = _physgunBeamObject.Components.Create<LineRenderer>();
            ConfigurePhysgunBeamRenderer(_physgunBeamRenderer);
        }

        return _physgunBeamRenderer.IsValid();
    }

    private void ConfigurePhysgunBeamRenderer(LineRenderer renderer)
    {
        if (!renderer.IsValid())
            return;

        renderer.Additive = true;
        renderer.AutoCalculateNormals = true;
        renderer.CastShadows = false;
        renderer.Color = new Gradient(new Gradient.ColorFrame(0.51890755f, new Color(1.74419f, 2.7907f, 3f, 1f)));
        renderer.CylinderSegments = 12;
        renderer.DepthFeather = 4f;
        renderer.Face = SceneLineObject.FaceMode.Camera;
        renderer.FogStrength = 1f;
        renderer.Lighting = false;
        renderer.Opaque = false;
        renderer.UseVectorPoints = true;
        renderer.VectorPoints = new List<Vector3> { Vector3.Zero, Vector3.Zero, Vector3.Forward * 128f, Vector3.Forward * 256f };
        renderer.Width = new Curve(new Curve.Frame(0f, 3.4f), new Curve.Frame(0.46773395f, 5f), new Curve.Frame(1f, 0f));

        var material = PhysgunBeamMaterial.IsValid()
            ? PhysgunBeamMaterial
            : Material.Load("weapons/physgun/physgun_beam.vmat");

        renderer.Texturing = renderer.Texturing with
        {
            Material = material,
            WorldSpace = true,
            UnitsPerTexture = 512
        };
    }

    private void UpdatePhysgunBeamEffects(Vector3 end, Vector3 endNormal, bool grabbed)
    {
        var transform = new Transform(end, Rotation.LookAt(endNormal.LengthSquared > 0.001f ? endNormal.Normal : Vector3.Up));

        if (grabbed)
        {
            if (_physgunBeamEndPointEffect.IsValid())
            {
                ITemporaryEffect.DisableLoopingEffects(_physgunBeamEndPointEffect);
                _physgunBeamEndPointEffect = null;
            }

            if (!_physgunBeamGrabEffect.IsValid() && PhysgunBeamGrabEffectPrefab.IsValid())
                _physgunBeamGrabEffect = PhysgunBeamGrabEffectPrefab.Clone(transform);

            if (_physgunBeamGrabEffect.IsValid())
                _physgunBeamGrabEffect.WorldTransform = transform;

            return;
        }

        if (_physgunBeamGrabEffect.IsValid())
        {
            _physgunBeamGrabEffect.Destroy();
            _physgunBeamGrabEffect = null;
        }

        if (!_physgunBeamEndPointEffect.IsValid() && PhysgunBeamEndPointEffectPrefab.IsValid())
            _physgunBeamEndPointEffect = PhysgunBeamEndPointEffectPrefab.Clone(transform);

        if (_physgunBeamEndPointEffect.IsValid())
            _physgunBeamEndPointEffect.WorldTransform = transform;
    }

    private void ClosePhysgunBeamEffects()
    {
        if (_physgunBeamEndPointEffect.IsValid())
        {
            ITemporaryEffect.DisableLoopingEffects(_physgunBeamEndPointEffect);
            _physgunBeamEndPointEffect = null;
        }

        if (_physgunBeamGrabEffect.IsValid())
        {
            _physgunBeamGrabEffect.Destroy();
            _physgunBeamGrabEffect = null;
        }
    }

    private void UpdatePhysgunBeamSound(Vector3 position)
    {
        if (!_physgunBeamSoundActive)
        {
            if (DefaultPhysgunBeamStartSound.IsValid())
                Sound.Play(DefaultPhysgunBeamStartSound, position);

            if (DefaultPhysgunBeamActiveLoopSound.IsValid())
                _physgunBeamActiveLoopSound = Sound.Play(DefaultPhysgunBeamActiveLoopSound, position);

            _physgunBeamSoundActive = true;
            return;
        }

        if (_physgunBeamActiveLoopSound is not null)
            _physgunBeamActiveLoopSound.Position = position;
    }

    private void StopPhysgunBeamSound()
    {
        _physgunBeamSoundActive = false;

        _physgunBeamActiveLoopSound?.Stop();
        _physgunBeamActiveLoopSound = null;
    }

    private void SetPhysgunBeamVisible(bool visible)
    {
        if (!_physgunBeamObject.IsValid())
            return;

        _physgunBeamObject.Enabled = visible;
        if (visible)
            return;

        StopPhysgunBeamSound();
        _physgunBeamPreviousDistance = 0f;
        _physgunBeamMiddleSpring = new Vector3.SpringDamped(0, 0);
        ClosePhysgunBeamEffects();
    }

    private void DestroyPhysgunBeamVisual()
    {
        StopPhysgunBeamSound();
        ClosePhysgunBeamEffects();

        if (_physgunBeamObject.IsValid())
            _physgunBeamObject.Destroy();

        _physgunBeamObject = null;
        _physgunBeamRenderer = null;
        _physgunBeamPreviousDistance = 0f;
        _physgunBeamMiddleSpring = new Vector3.SpringDamped(0, 0);
        _localPhysgunBeamOverrideSet = false;
        _localPhysgunBeamOverrideActive = false;
        _localPhysgunBeamOverrideGrabbed = false;
    }


    public void TakeBox( int amount )
    {
        Money += amount;
    
        RpcNotifyTakeBox( amount );
    }
 
    [Rpc.Owner]
    private void RpcNotifyTakeBox( int amount )
    {
        Notification.Info( GameLocalization.Format( "notify.money.looted", "You looted ${0}", amount ), 3.5f );
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
            NotifyMoneyResult( caller, GameLocalization.Phrase( "notify.player.not_ready", "Your player is not ready." ), false );
            return;
        }

        if ( amount <= 0 )
        {
            NotifyMoneyResult( caller, GameLocalization.Phrase( "notify.money.invalid_amount", "Enter a valid amount." ), false );
            return;
        }

        if ( player.Money < amount )
        {
            NotifyMoneyResult( caller, GameLocalization.Phrase( "ui.shop.not_enough_money", "Not enough money" ), false );
            return;
        }

        if ( !player.MoneyDropPrefab.IsValid() )
        {
            NotifyMoneyResult( caller, GameLocalization.Phrase( "notify.money.prefab_missing", "Money prefab is not configured." ), false );
            return;
        }

        if ( !TrySpawnDroppedMoney( player, caller, amount ) )
        {
            NotifyMoneyResult( caller, GameLocalization.Phrase( "notify.money.drop_failed", "Could not drop money." ), false );
            return;
        }

        player.Money -= amount;
        Log.Info( $"[RpcRequestDropMoney] {caller.DisplayName} ({caller.SteamId}) dropped ${amount}" );
        NotifyMoneyResult( caller, GameLocalization.Format( "notify.money.dropped", "You dropped ${0}.", amount ), true );
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
            NotifyMoneyResult( caller, GameLocalization.Phrase( "notify.player.not_ready", "Your player is not ready." ), false );
            return;
        }

        if ( amount <= 0 )
        {
            NotifyMoneyResult( caller, GameLocalization.Phrase( "notify.money.invalid_amount", "Enter a valid amount." ), false );
            return;
        }

        if ( player.Money < amount )
        {
            NotifyMoneyResult( caller, GameLocalization.Phrase( "ui.shop.not_enough_money", "Not enough money" ), false );
            return;
        }

        if ( targetSteamId == caller.SteamId.Value )
        {
            NotifyMoneyResult( caller, GameLocalization.Phrase( "notify.money.cannot_transfer_self", "You cannot transfer money to yourself." ), false );
            return;
        }

        var target = FindPlayerBySteamId( targetSteamId );
        if ( !target.IsValid() )
        {
            NotifyMoneyResult( caller, GameLocalization.Phrase( "notify.money.transfer_target_not_found", "Transfer target not found." ), false );
            return;
        }

        if ( !CanTransferToTarget( player, target ) )
        {
            NotifyMoneyResult( caller, GameLocalization.Phrase( "notify.money.target_too_far", "Player is too far away or not in front of you." ), false );
            return;
        }

        player.Money -= amount;
        target.Money += amount;

        var targetConnection = target.GameObject.Network.Owner;
        Log.Info( $"[RpcRequestTransferMoney] {caller.DisplayName} ({caller.SteamId}) transferred ${amount} to {GetConnectionName( targetConnection )} ({targetConnection?.SteamId})" );
        NotifyMoneyResult( caller, GameLocalization.Format( "notify.money.transferred", "You transferred ${0} to {1}.", amount, GetConnectionName( targetConnection ) ), true );

        if ( targetConnection is not null )
            NotifyMoneyResult( targetConnection, GameLocalization.Format( "notify.money.received", "{0} transferred ${1} to you.", caller.DisplayName, amount ), true );
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

    public void UnregisterSpawnedProp( PropCustom prop )
    {
        if ( !prop.IsValid() )
            return;

        _ownedPropSpawnStack.RemoveAll( x => !x.IsValid() || x == prop );
    }

    private void TryUndoLastOwnedProp()
    {
        if ( IsProxy )
            return;
        if ( !Input.Pressed( "Undo" ) )
            return;

        if ( Networking.IsHost )
        {
            HostUndoLastOwnedProp( GameObject.Network.Owner );
            return;
        }

        RpcRequestUndoLastOwnedProp();
    }

    [Rpc.Host]
    private void RpcRequestUndoLastOwnedProp()
    {
        if ( !Networking.IsHost )
            return;

        var caller = Rpc.Caller;
        if ( caller is null )
            return;

        var player = FindPlayerBySteamId( caller.SteamId.Value );
        if ( !player.IsValid() )
            return;

        player.HostUndoLastOwnedProp( caller );
    }

    private void HostUndoLastOwnedProp( Connection connection )
    {
        if ( !Networking.IsHost )
            return;

        var prop = GetLastOwnedProp();
        if ( !prop.IsValid() )
        {
            NotifyInventoryResult( connection, GameLocalization.Phrase( "notify.props.none_spawned", "You have no spawned props." ), false );
            return;
        }

        var propObject = prop.GameObject;
        var propName = !propObject.IsValid() || string.IsNullOrWhiteSpace( propObject.Name ) ? "Prop" : propObject.Name;
        propObject?.Destroy();
        NotifyInventoryResult( connection, GameLocalization.Format( "notify.props.removed", "Removed prop: {0}", propName ), true );
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

            _ownedPropSpawnStack.RemoveAt( i );
            return prop;
        }

        return null;
    }

    private static string GetConnectionName( Connection connection )
    {
        return string.IsNullOrWhiteSpace( connection?.DisplayName ) ? GameLocalization.Phrase( "common.player_dative", "player" ) : connection.DisplayName;
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
                RpcSetHoldTypeHandedness(Renderer, 0);
                RpcSetPlayerReload(false);
            }

            // Время считаем на клиенте; по истечении просим хост освободить.
            if ((float)_arrestTimeUntilRelease <= 0f)
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

        if (Vector3.DistanceBetween(attacker.WorldPosition, target.WorldPosition) > JobManager.Instance.ArrestInteractRange)
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

        if (Vector3.DistanceBetween(attacker.WorldPosition, target.WorldPosition) > JobManager.Instance.ArrestInteractRange)
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
        ArrestTimeUntilRelease = JobManager.Instance.ArrestDurationSeconds;
        HostSetEquippedWeaponItemId(null);

        var randomSpawn = Random.Shared.FromList(JobManager.Instance.ArrestSpawnPoint.Children);

        var pos = randomSpawn.IsValid() ? randomSpawn.WorldPosition : WorldPosition;
        var rot = randomSpawn.IsValid() ? randomSpawn.WorldRotation : WorldRotation;
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
