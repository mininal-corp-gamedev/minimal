using Sandbox;

public sealed class SpawnManager : Component
{
    public static SpawnManager Instance { get; private set; }

    [Property] public GameObject PlayerSpawns { get; set; }
    [Property] public string PlayerSpawnsName { get; set; } = "Spawn Players";

    protected override void OnAwake()
    {
        if (Instance == null)
            Instance = this;
    }

    protected override void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public GameObject GetRandomPlayerSpawn()
    {
        var playerSpawns = ResolvePlayerSpawns();
        if (!playerSpawns.IsValid())
            return null;

        var children = playerSpawns.Children
            .Where(child => child.IsValid())
            .ToList();

        if (children.Count == 0)
            return playerSpawns;

        var randomIndex = Game.Random.Int(0, children.Count - 1);
        return children[randomIndex];
    }

    private GameObject ResolvePlayerSpawns()
    {
        if (PlayerSpawns.IsValid())
            return PlayerSpawns;

        if (string.IsNullOrWhiteSpace(PlayerSpawnsName))
            return null;

        PlayerSpawns = Scene.GetAllObjects(true)
            .FirstOrDefault(obj => obj.IsValid() && string.Equals(obj.Name, PlayerSpawnsName, StringComparison.OrdinalIgnoreCase));

        if (PlayerSpawns.IsValid())
            return PlayerSpawns;

        PlayerSpawns = Scene.GetAllObjects(true)
            .FirstOrDefault(obj => obj.IsValid() && obj.Name?.StartsWith(PlayerSpawnsName, StringComparison.OrdinalIgnoreCase) == true);

        return PlayerSpawns;
    }
}
