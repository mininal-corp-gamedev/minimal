[AssetType(Name = "Job Definition", Extension = "job", Category = "Minimal")]
public sealed class JobDefinition : GameResource
{
    [Property] public string Id { get; set; } = "id";
    [Property] public string Header { get; set; } = "Unknow";
    [Property] public string Category { get; set; } = "Other";
    [Property] public string Description { get; set; } = "";
    [Property] public int MaxCount { get; set; } = 0; // 0 for unlimited
    [Property] public int Salary { get; set; } = 0;
    [Property] public bool Vote { get; set; } = false;
    [Property] public bool CanDemote { get; set; } = true;

    [Property] public bool HasPostSpawned { get; set; } = false;
    [Property] public bool HasPostDemote { get; set; } = false;
    [Property] public bool HasPostJoined { get; set; } = false;

    protected override Bitmap CreateAssetTypeIcon(int width, int height)
    {
        return CreateSimpleAssetTypeIcon("plus", width, height, "#ffffff", "black");
    }
}