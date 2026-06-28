[AssetType(Name = "Job Definition", Extension = "job", Category = "Minimal")]
public sealed class JobDefinition : GameResource
{
    public string Id => ResourceName;
    [Property] public string Header { get; set; } = "Unknow";
    [Property] public string Category { get; set; } = "Other";
    [Property] public string Description { get; set; } = "";
    [Property] public int MaxCount { get; set; } = 0; // 0 for unlimited
    [Property] public int Salary { get; set; } = 0;
    [Property] public bool Vote { get; set; } = false;
    [Property] public bool CanDemote { get; set; } = true;
    [Property] public bool CanArrest { get; set; } = true;
    [Property] public bool CanBuyShop { get; set; } = true;
    [Property] public bool CanSpawnProp { get; set; } = true;
    [Property] public Color Color { get; set; } = Color.White;
    [Property] public List<JobDefinition> FromJobs { get; set; } = new();
    [Property] public List<string> WorkshopClothing { get; set; } = new();

    [Property, Group("Events")] public bool HasPostSpawned { get; set; } = false;
    [Property, Group("Events")] public bool HasPostDemote { get; set; } = false;
    [Property, Group("Events")] public bool HasPostJoined { get; set; } = false;

    protected override Bitmap CreateAssetTypeIcon(int width, int height)
    {
        return CreateSimpleAssetTypeIcon("person", width, height, "#db2175", "#212121");
    }
}