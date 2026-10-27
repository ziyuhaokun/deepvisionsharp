namespace RuntimePackage.Engine.Packaging.Build;

public sealed record BuildProgressUpdate(
    BuildStage Stage,
    string StageDisplayName,
    int OverallPercent,
    int StagePercent,
    int ProcessedItems,
    int TotalItems,
    long ProcessedBytes,
    long TotalBytes,
    string Message,
    string LogFilePath,
    bool IsError);
