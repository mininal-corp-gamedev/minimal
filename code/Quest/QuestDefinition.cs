[AssetType(Name = "Quest Definition", Extension = "quest", Category = "Quest")]
public sealed class QuestDefinition : GameResource
{
    public string Id => ResourceName;
    [Property, Description("Заголовок квеста, отображаемый в интерфейсе.")]
    public string Header { get; set; } = "Unknow";

    [Property, Description("Категория квеста. Используется правилом CanGetSameCategoryQuest.")]
    public string Category { get; set; } = "Other";

    [Property, Description("Подробное описание квеста.")]
    public string Description { get; set; } = "";

    [Property, Description("Если выключено — игрок не сможет взять этот квест, пока у него активен другой квест той же Category.")]
    public bool CanGetSameCategoryQuest { get; set; } = true;

    [Property, Description("Если включено — игрок сможет взять этот квест ещё раз после его завершения.")]
    public bool CanRepeat { get; set; } = false;

    [Property, Description("Список задач квеста, выполняемых по порядку.")]
    public List<QuestTaskDefinition> QuestTasks { get; set; } = new();

    [Property, Group("Events"), Description("Включает вызов IQuestHandler.OnQuestCompleted при завершении квеста.")]
    public bool HasPostCompleted { get; set; } = false;

    [Property, Group("Events"), Description("Включает вызов IQuestHandler.OnQuestCanceled при отмене квеста.")]
    public bool HasPostCanceled { get; set; } = false;

    protected override Bitmap CreateAssetTypeIcon(int width, int height)
    {
        return CreateSimpleAssetTypeIcon("person", width, height, "#db2175", "#34eb64");
    }
}