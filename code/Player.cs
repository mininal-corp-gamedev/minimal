using Sandbox;
using System;
using static Sandbox.Gizmo;

public sealed class Player : Component, Component.IDamageable
{
    public static Player Local { get; private set; }

    [Property] public PlayerController Controller { get; private set; }
    [Property] public Dresser Dresser { get; private set; }
    [Property] public PlayerWorldHud WorldHud { get; private set; }
    [Property, Category("Sounds")] public SoundEvent HitSound { get; set; }

    [Sync] public float Health { get; set; } = 100f;
    [Sync] public float MaxHealth { get; set; } = 100f;
    [Sync] public int Money { get; set; } = 0;
    public int CactusCount { get; set; } = 0;
    public bool IsAlive => Health > 0;

    public Weapon CurrentWeapon { get; private set; }

    public void Spawn()
    {
        if (IsProxy) return;

        var spawnPoint = SpawnManager.Instance?.GetRandomPlayerSpawn();
        if (!spawnPoint.IsValid()) return;

        Health = MaxHealth;
        WorldHud?.WorldHudRefresh();
        WorldPosition = spawnPoint.WorldPosition;
        WorldRotation = spawnPoint.WorldRotation;
    }

    public void OnDamage(in DamageInfo dmgInfo)
    {
        // Локальные источники урона (окружение и т.п.) — только на авторитетной копии.
        if (IsProxy) return;

        TakeDamageFromWeapon(dmgInfo.Damage);
    }

    /// <summary>
    /// Урон от оружия другого игрока. <c>[Rpc.Owner]</c> доставляет вызов на машину владельца этого Player,
    /// где <c>[Sync] Health</c> можно записать и изменение синхронизируется всем.
    /// </summary>
    public void TakeDamageFromWeapon(float damage)
    {
        RpcTakeDamageFromWeapon(damage);
    }

    [Rpc.Owner]
    private void RpcTakeDamageFromWeapon(float damage)
    {
        const float maxPerRpc = 200f;
        if (damage <= 0f) return;
        damage = Math.Min(damage, maxPerRpc);

        Health = Math.Max(0f, Health - damage);
        WorldHud?.WorldHudRefresh();

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

            return;
        }

        CurrentWeapon = wep;
        CurrentWeapon.GameObject.Enabled = true;
    }

    private void CheckChangeWeapon()
    {
        if (IsProxy) return;

        if (Input.Pressed("Slot1"))
            SwitchWeapon(WeaponManager.Instance.Pickaxe);
        else if (Input.Pressed("Slot2"))
            SwitchWeapon(WeaponManager.Instance.Usp);
        else if (Input.Pressed("Slot3"))
            SwitchWeapon(WeaponManager.Instance.Mp5);
        else if (Input.Pressed("Slot4"))
            SwitchWeapon(WeaponManager.Instance.M4A1);
        else if (Input.Pressed("Slot5"))
            SwitchWeapon();
        else if (Input.Pressed("Slot6"))
            SwitchWeapon();
        else if (Input.Pressed("Slot7"))
            SwitchWeapon();
        else if (Input.Pressed("Slot8"))
            SwitchWeapon();
        else if (Input.Pressed("Slot9"))
            SwitchWeapon();
        else if (Input.Pressed("Slot0"))
            SwitchWeapon();
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

    private void NetworkInit()
    {
        if (IsProxy) return;

        Spawn();
        SetupWorldHud();
        DressForHost(Dresser);
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
        CheckChangeWeapon();
    }

    protected override void OnDestroy()
    {
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
}
