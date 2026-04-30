using Sandbox;

public sealed class SpawnManager : Component
{
    public static SpawnManager Instance { get; private set; }

    [Property] public GameObject PlayerSpawns { get; set; }

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

    public GameObject GetRandomPlayerSpawn()
    {
        if (!PlayerSpawns.IsValid())
            return null;

        var children = PlayerSpawns.Children
            .Where(child => child.IsValid())
            .ToList();

        if (children.Count == 0)
            return PlayerSpawns;

        var randomIndex = Game.Random.Int(0, children.Count - 1);
        return children[randomIndex];
    }
}
