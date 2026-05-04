using Sandbox;

public sealed class JobManager : Component
{
    public static JobManager Instance { get; private set; }

    [Property, Category("Arrest")] public GameObject ArrestSpawnPoint { get; set; }
    [Property, Category("Arrest")] public float ArrestDurationSeconds { get; set; } = 120f;
    [Property, Category("Arrest")] public float ArrestInteractRange { get; set; } = 110f; //todo transfer to WeaponHandcuff

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
}
