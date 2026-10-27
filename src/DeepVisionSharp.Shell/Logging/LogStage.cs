namespace DeepVisionSharp.Shell.Logging;

/// <summary>
/// Application log partition for multi-file routing under %LOCALAPPDATA%\DeepVisionSharp\Logs\.
/// Job product logs (e.g. train.log) are intentionally separate.
/// </summary>
public enum LogStage
{
    App,
    Training,
    Data,
    Model,
    Inference,
    Imaging,
    System,
}

public static class LogStageNames
{
    public static string ToDirectoryName(this LogStage stage) => stage switch
    {
        LogStage.App => "app",
        LogStage.Training => "training",
        LogStage.Data => "data",
        LogStage.Model => "model",
        LogStage.Inference => "inference",
        LogStage.Imaging => "imaging",
        LogStage.System => "system",
        _ => "system",
    };

    public static string ToLabel(this LogStage stage) => stage switch
    {
        LogStage.App => "App",
        LogStage.Training => "Training",
        LogStage.Data => "Data",
        LogStage.Model => "Model",
        LogStage.Inference => "Inference",
        LogStage.Imaging => "Imaging",
        LogStage.System => "System",
        _ => "System",
    };
}
