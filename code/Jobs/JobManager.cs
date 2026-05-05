using Sandbox;

public sealed class JobManager : Component
{
    public static JobManager Instance { get; private set; }

    [Property, Category("Arrest")] public GameObject ArrestSpawnPoint { get; set; }
    [Property, Category("Arrest")] public float ArrestDurationSeconds { get; set; } = 120f;
    [Property, Category("Arrest")] public float ArrestInteractRange { get; set; } = 110f; //todo transfer to WeaponHandcuff

    [Property, Category("General")] public float SalaryDelay { get; set; } = 500f;
    [Property, Category("General")] public JobDefinition DemoteJob { get; set; }

    public TimeUntil SalaryTime { get; private set; }

    protected override void OnAwake()
    {
        if (Instance == null)
            Instance = this;

        SalaryTime = SalaryDelay;
    }

    protected override void OnDestroy()
    {
        if (Instance != null)
            Instance = null;
    }

    protected override void OnFixedUpdate()
    {
        if (!Networking.IsHost) return;
        if (!SalaryTime) return;

        SalaryTime = SalaryDelay;
        Payday();
    }

    public void Payday()
    {
        if (!Networking.IsHost) return;

        foreach (var player in Scene.GetAll<Player>())
        {
            if (player.Components.TryGet<PlayerJob>(out var job, FindMode.EverythingInSelfAndParent))
            {
                player.Money += job.JobDefinition.Salary;
                //todo notify player about salary
            }
        }
    }
}
