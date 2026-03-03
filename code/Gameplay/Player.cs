using Sandbox;
using SilentEcho.Storage;
using System;
using System.Threading.Tasks;

[Description("Default player stats and actions for Atlantica"), Icon("face")]
public sealed class Player : Component, Component.IDamageable
{
	public static Player Instance { get; private set; }

    [Property, Feature("Components")] public PlayerController Controller { get; set; }
    [Property, Feature("Components")] public HUD Hud { get; set; }
    [Property, Feature("Components")] public ModelPhysics Ragdoll { get; set; }
    [Property, Feature("Components")] public Inventory Inventory { get; set; }

    private int _money = 15000;
    [Property, Feature("Stats")] public int Money
    {
        get => _money;
        set
        {
            if (value < 0) value = 0;

            int _old = _money;
            _money = value;

            OnMoneyChanged?.Invoke(value, _old);
        }
    }

    private float _health = 100f;
    [Property, Feature("Stats"), Step(1f)] public float Health 
	{ 
		get => _health;
		set 
		{ 
			if (value < 0) value = 0;

			float _old = _health;
			_health = value;

			OnHealthChanged?.Invoke(value, _old);

			if (value == 0)
				Die();
        } 
	}
    [Property, Feature("Stats"), Step(1f)] public float MaxHealth { get; set; } = 100f;
	public bool IsAlive { get; private set; } = true;

    public Action<float, float> OnMoneyChanged { get; set; }
    public Action<float, float> OnHealthChanged { get; set; }
    public Action OnDied { get; set; }

	private Transform _spawn;

    public void Die()
	{
		if (!IsAlive) return;

        IsAlive = false;
        Hud.Hide();
        Ragdoll.Enabled = true;

        OnDied?.Invoke();

		_ = SpawnAsync(2f);
    }

	public void Spawn()
	{
		Ragdoll.Enabled = false;

		WorldTransform = _spawn;
		Health = MaxHealth;

		IsAlive = true;
	}

	public async Task SpawnAsync(float delay)
	{
		await Task.DelaySeconds(delay);

		Spawn();
	}

	private void Init()
	{
		_spawn = WorldTransform;
    }

	private void CreateSingleton()
	{
		if (Instance == null)
			Instance = this;
	}

	private void RemoveSingleton()
	{
		if (Instance != null)
			Instance = null;
    }

	private void DamageHandle(in DamageInfo dmgInfo)
	{
		if (!IsAlive) return;

		Hud.Show(2f);

		Health -= dmgInfo.Damage;

		Log.Info($"Damage {dmgInfo.Damage}");
	}
	
    protected override void OnAwake()
    {
		CreateSingleton();
    }

    protected override void OnStart()
	{
		Init();
    }

    protected override void OnDestroy()
    {
		RemoveSingleton();
    }

    public void OnDamage(in DamageInfo dmgInfo)
    {
		DamageHandle(dmgInfo);
    }
}
