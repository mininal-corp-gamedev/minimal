[AssetType(Name = "Prop Definition", Extension = "prop", Category = "Minimal")]
public sealed class PropDefinition : GameResource
{
    public string Id => ResourceName;
    [Property] public string Header { get; set; } = "Unknow";
    [Property] public string Category { get; set; } = "Other";
    [Property] public string Description { get; set; } = "";
    [Property] public int Price { get; set; } = 100;
    [Property] public string Ident { get; set; } = "facepunch.couch";

    protected override Bitmap CreateAssetTypeIcon(int width, int height)
    {
        return CreateSimpleAssetTypeIcon("plus", width, height, "#ffffff", "orange");
    }
}