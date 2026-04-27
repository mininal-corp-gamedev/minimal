using Ambi.Storage;

namespace Minimal.Shop;

[AssetType(Name = "Shop Definition", Extension = "shop", Category = "Minimal")]
public sealed class ShopDefinition : GameResource
{
    [Property] public string Id { get; set; } = "id";
    [Property] public string Header { get; set; } = "Unknow";
    [Property] public string Category { get; set; } = "Other";
    [Property] public string Description { get; set; } = "";
    [Property] public ItemDefinition ItemDefinition { get; set; }
    [Property] public int Price { get; set; } = 10;
    [Property, ResourceType("vtex")] public string IconPath { get; set; } = "icons/items/default.vtex";
    [Property] public Model Model { get; set; }
    [Property] public GameObject SpawnPrefab { get; set; }
    [Property] public bool IsAllowEveryone { get; set; } = true;
    [Property, ShowIf("IsAllowEveryone", false)] public List<JobDefinition> JobsAllow { get; set; } = new();

    [Property] public bool HasPostPurchased { get; set; } = false;

    protected override Bitmap CreateAssetTypeIcon(int width, int height)
    {
        return CreateSimpleAssetTypeIcon("backpack", width, height, "#ffffff", "red");
    }
}
