public class Quest
{
    public QuestDefinition QuestDefinition { get; set; }
    public QuestTaskDefinition CurrentQuestTask { get; set; }
    public int CurrentCount { get; set; } = 0;
    public bool IsFinish { get; set; } = false;

    public int CurrentTaskIndex
    {
        get
        {
            if (QuestDefinition?.QuestTasks == null || CurrentQuestTask == null)
                return -1;
            return QuestDefinition.QuestTasks.IndexOf(CurrentQuestTask);
        }
    }

    public QuestSnapshot ToSnapshot()
    {
        return new QuestSnapshot
        {
            QuestId = QuestDefinition?.Id ?? string.Empty,
            TaskId = CurrentQuestTask?.Id ?? string.Empty,
            CurrentCount = CurrentCount,
            IsFinish = IsFinish,
        };
    }

    public static Quest FromSnapshot(QuestSnapshot s)
    {
        var def = QuestDatabase.FindQuestById(s.QuestId);
        if (def == null)
            return null;

        QuestTaskDefinition task = null;
        if (def.QuestTasks != null && def.QuestTasks.Count > 0)
        {
            task = def.QuestTasks.Find(t => t != null && t.Id == s.TaskId);
            if (task == null && !s.IsFinish)
                task = def.QuestTasks[0];
        }

        return new Quest
        {
            QuestDefinition = def,
            CurrentQuestTask = task,
            CurrentCount = s.CurrentCount,
            IsFinish = s.IsFinish,
        };
    }
}