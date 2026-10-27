namespace DeepVisionSharp.Domain.Entities;

public enum TrainedModelStatus
{
    Creating,
    Pending,
    Training,
    Ready,
    Published,
    Archived
}

public static class TrainedModelStatuses
{
    public static string ToPersistedText(TrainedModelStatus status) =>
        status switch
        {
            TrainedModelStatus.Creating => "创建中",
            TrainedModelStatus.Pending => "待训练",
            TrainedModelStatus.Training => "训练中",
            TrainedModelStatus.Ready => "训练完成",
            TrainedModelStatus.Published => "已发布",
            TrainedModelStatus.Archived => "归档",
            _ => status.ToString()
        };

    public static TrainedModelStatus ParsePersistedText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return TrainedModelStatus.Pending;
        }

        return value.Trim() switch
        {
            "创建中" => TrainedModelStatus.Creating,
            "待训练" => TrainedModelStatus.Pending,
            "训练中" => TrainedModelStatus.Training,
            "训练完成" or "已训练" => TrainedModelStatus.Ready,
            "已发布" => TrainedModelStatus.Published,
            "归档" => TrainedModelStatus.Archived,
            _ when Enum.TryParse<TrainedModelStatus>(value, ignoreCase: true, out var parsed) => parsed,
            _ => TrainedModelStatus.Pending
        };
    }

    /// <summary>
    /// Ready and Published models can run inference; Creating/Pending/Training/Archived cannot.
    /// Single-image inference, gallery batch inference and labeling auto-inference share this rule.
    /// </summary>
    public static bool CanRunInference(TrainedModelStatus status) =>
        status is TrainedModelStatus.Ready or TrainedModelStatus.Published;

    public static TrainedModelStatus Normalize(TrainedModelStatus status, bool hasRunningTraining)
    {
        if (status == TrainedModelStatus.Training && !hasRunningTraining)
        {
            return TrainedModelStatus.Pending;
        }

        return status;
    }

    public static string ToDisplayText(TrainedModelStatus status) => ToPersistedText(status);
}
