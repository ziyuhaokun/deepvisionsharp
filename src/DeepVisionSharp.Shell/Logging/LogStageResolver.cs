namespace DeepVisionSharp.Shell.Logging;

/// <summary>
/// Maps <see cref="ILogger{T}"/> category names (full type names) to <see cref="LogStage"/>.
/// First matching rule wins; order encodes intentional precedence (e.g. Training before Model).
/// </summary>
public static class LogStageResolver
{
    public static LogStage Resolve(string? categoryName)
    {
        if (string.IsNullOrWhiteSpace(categoryName))
        {
            return LogStage.System;
        }

        // Training lifecycle before Model (e.g. *TrainingService*, *TrainingBackend*)
        if (ContainsAny(
                categoryName,
                "Training",
                "TrainVariation"))
        {
            return LogStage.Training;
        }

        // Inference engines / UI runners
        if (ContainsAny(
                categoryName,
                "Inference"))
        {
            return LogStage.Inference;
        }

        // Model library / artifacts / evaluation (non-training)
        if (ContainsAny(
                categoryName,
                "Artifact",
                "TrainedModel",
                "ModelMetadata",
                "ModelCheckpoint",
                "ModelsViewModel",
                "ModelsList",
                "ModelsEditor",
                "ModelType"))
        {
            return LogStage.Model;
        }

        // Gallery / annotation / export / migration / project transfer
        if (ContainsAny(
                categoryName,
                "Gallery",
                "Annotation",
                "SampleMigration",
                "DatasetExport",
                "ClassificationDataset",
                "ProjectSqliteTransfer",
                "ProjectLabel",
                "DashboardProject",
                "SampleThumbnail",
                "SampleFile"))
        {
            return LogStage.Data;
        }

        // Imaging engines (OpenCV) outside training orchestration
        if (ContainsAny(
                categoryName,
                "Imaging",
                "OpenCv",
                "PerformanceProfiler"))
        {
            return LogStage.Imaging;
        }

        // Shell UI / host (after domain rules so Training*ViewModel etc. already classified)
        if (categoryName.StartsWith("DeepVisionSharp.Shell", StringComparison.Ordinal)
            || ContainsAny(
                categoryName,
                "UnhandledExceptionReporter",
                "MainWindow",
                "SettingsViewModel",
                "ShellTaskCenter",
                "ShellSettings",
                "ShellAsync",
                "ShellSnackbar",
                "AppDialog",
                "AppNavigation",
                "WpfAppTheme",
                "WorkspaceSession",
                "LocalDump",
                "StorageCleanup",
                "DataIntegrity"))
        {
            return LogStage.App;
        }

        // Pipeline / persistence / hosted services / default
        if (ContainsAny(
                categoryName,
                "Behaviors",
                "Persistence",
                "HostedService",
                "Sqlite",
                "ResultFailure",
                "MediatR"))
        {
            return LogStage.System;
        }

        return LogStage.System;
    }

    private static bool ContainsAny(string categoryName, params string[] tokens)
    {
        foreach (var token in tokens)
        {
            if (categoryName.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
