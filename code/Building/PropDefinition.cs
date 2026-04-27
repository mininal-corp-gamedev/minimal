[AssetType(Name = "Prop Definition", Extension = "prop", Category = "Minimal")]
public sealed class PropDefinition : GameResource
{
    [Property] public string Id { get; set; } = "id";
    [Property] public string Header { get; set; } = "Unknow";
    [Property] public string Category { get; set; } = "Other";
    [Property] public string Description { get; set; } = "";
    [Property] public GameObject Prefab { get; set; }

    protected override Bitmap CreateAssetTypeIcon(int width, int height)
    {
        return CreateSimpleAssetTypeIcon("plus", width, height, "#ffffff", "orange");
    }
}