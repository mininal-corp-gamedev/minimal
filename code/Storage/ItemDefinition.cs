namespace Ambi.Storage;

[AssetType( Name = "Item Definition", Extension = "item", Category = "Minimal")]
public sealed class ItemDefinition : GameResource
{
    public string Id => ResourceName;
    [Property] public string Category { get; set; } = "Other";
    [Property] public string Header { get; set; } = "";
    [Property] public string Description { get; set; } = "";
    [Property] public int MaxCount { get; set; } = 1;
    [Property, ResourceType("vtex")] public string IconPath { get; set; } = "icons/items/default.vtex";
    [Property] public Model Model { get; set; }

    [Property] public bool CanUse { get; set; } = false;
    [Property] public bool CanDrop { get; set; } = true;
    [Property] public bool IsJobItem { get; set; } = false;
    [Property] public bool CanSave { get; set; } = true;

    protected override Bitmap CreateAssetTypeIcon(int width, int height)
    {
        return CreateSimpleAssetTypeIcon("backpack", width, height, "#ffffff", "black");
    }
}
