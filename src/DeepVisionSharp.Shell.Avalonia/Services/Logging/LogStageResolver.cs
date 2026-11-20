using DeepVisionSharp.Presentation.Logging;

namespace DeepVisionSharp.Shell.Avalonia.Services.Logging;

/// <summary>
/// Maps <see cref="ILogger{T}"/> category names (full type names) to <see cref="LogStage"/>.
/// First matching rule wins; order encodes intentional precedence (e.g. Training before Model).
/// </summary>
/// <remarks>
/// 与 WPF Shell 的 <c>DeepVisionSharp.Shell.Logging.LogStageResolver</c> 规则一致：判定用的是
/// <c>ILogger</c> 的 category 名（业务类型全名），两套 Shell 共用同一批业务类型，所以规则可直接复用。
/// </remarks>
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
