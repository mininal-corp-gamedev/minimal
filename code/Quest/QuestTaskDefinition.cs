[AssetType(Name = "Quest Task Definition", Extension = "qtask", Category = "Quest")]
public sealed class QuestTaskDefinition : GameResource
{
    public string Id => ResourceName;
    [Property, Description("Заголовок задачи, отображаемый в интерфейсе.")]
    public string Header { get; set; } = "Unknow";

    [Property, Description("Подробное описание задачи.")]
    public string Description { get; set; } = "";

    [Property, Description("Сколько раз нужно выполнить действие, чтобы задача завершилась. Если > 1 — в HUD отображается прогресс current/total.")]
    public int Count { get; set; } = 1;

    [Property, Group("Events"), Description("Включает вызов IQuestHandler.OnTaskCompleted при завершении этой задачи.")]
    public bool HasPostCompleted { get; set; } = false;

    protected override Bitmap CreateAssetTypeIcon(int width, int height)
    {
        return CreateSimpleAssetTypeIcon("person", width, height, "#d8ebf2", "#fcba03");
    }
}