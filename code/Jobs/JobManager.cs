using Sandbox;

public sealed class JobManager : Component
{
    public static JobManager Instance { get; private set; }

    [Property, Category("Arrest")] public GameObject ArrestSpawnPoint { get; set; }
    [Property, Category("Arrest")] public string ArrestSpawnPointName { get; set; } = "Arrest Spawn";
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
#if SERVER
        if (!Networking.IsHost) return;
        if (!SalaryTime) return;

        SalaryTime = SalaryDelay;
        Payday();
#endif
    }

    public void Payday()
    {
#if SERVER
        if (!Networking.IsHost) return;

        foreach (var player in Scene.GetAll<Player>())
        {
            if (player.Components.TryGet<PlayerJob>(out var job, FindMode.EverythingInSelfAndParent))
            {
                player.Money += job.JobDefinition.Salary;
                //todo notify player about salary
            }
        }
#endif
    }

    public GameObject GetRandomArrestSpawn()
    {
        var arrestSpawns = ResolveArrestSpawnPoint();
        if (!arrestSpawns.IsValid())
            return null;

        var children = arrestSpawns.Children
            .Where(child => child.IsValid())
            .ToList();

        if (children.Count == 0)
            return arrestSpawns;

        var randomIndex = Game.Random.Int(0, children.Count - 1);
        return children[randomIndex];
    }

    private GameObject ResolveArrestSpawnPoint()
    {
        if (ArrestSpawnPoint.IsValid())
            return ArrestSpawnPoint;

        if (string.IsNullOrWhiteSpace(ArrestSpawnPointName))
            return null;

        ArrestSpawnPoint = Scene.GetAllObjects(true)
            .FirstOrDefault(obj => obj.IsValid() && string.Equals(obj.Name, ArrestSpawnPointName, StringComparison.OrdinalIgnoreCase));

        if (ArrestSpawnPoint.IsValid())
            return ArrestSpawnPoint;

        ArrestSpawnPoint = Scene.GetAllObjects(true)
            .FirstOrDefault(obj => obj.IsValid() && obj.Name?.StartsWith(ArrestSpawnPointName, StringComparison.OrdinalIgnoreCase) == true);

        return ArrestSpawnPoint;
    }
}
