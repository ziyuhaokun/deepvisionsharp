using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Domain.Common;

namespace DeepVisionSharp.Infrastructure.Services;

public sealed class TrainingWorkspacePlanner : ITrainingWorkspacePlanner
{
    private readonly IProjectFileStore _projectFileStore;

    public TrainingWorkspacePlanner(IProjectFileStore projectFileStore)
    {
        _projectFileStore = projectFileStore;
    }

    public TrainingWorkspacePlan CreateRunPlan(Guid projectId, Guid jobId, string modelName)
    {
        var runsRoot = _projectFileStore.GetTrainingRunsRoot(projectId, "default");
        Directory.CreateDirectory(runsRoot);

        // Task-level uniqueness: jobId guarantees two runs never share an OutputRoot,
        // even when the same model name starts twice within the same second.
        var autoVersion = $"{SanitizeSegment(modelName)}_{DateTime.Now:yyyyMMdd_HHmmss}_{jobId:N}";
        var version = SanitizeSegment(autoVersion);
        var outputRoot = _projectFileStore.GetTrainingRunPath(projectId, version);
        Directory.CreateDirectory(outputRoot);

        var logFile = _projectFileStore.GetTrainingLogPath(projectId, version);
        var vocRoot = Path.Combine(outputRoot, "voc");

        return new TrainingWorkspacePlan(runsRoot, version, outputRoot, logFile, vocRoot);
    }

    public Result<TrainingWorkspacePlan> ReuseExistingRun(string outputRoot, string logFile)
    {
        if (string.IsNullOrWhiteSpace(outputRoot) || !Directory.Exists(outputRoot))
        {
            return Result<TrainingWorkspacePlan>.Failure("续训输出目录不存在");
        }

        if (string.IsNullOrWhiteSpace(logFile))
        {
            return Result<TrainingWorkspacePlan>.Failure("续训日志路径无效");
        }

        var resolvedOutput = Path.GetFullPath(outputRoot);
        var resolvedLog = Path.GetFullPath(logFile);
        var version = Path.GetFileName(resolvedOutput.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(version))
        {
            return Result<TrainingWorkspacePlan>.Failure("续训输出目录名无效");
        }

        var runsRoot = Path.GetDirectoryName(resolvedOutput);
        if (string.IsNullOrWhiteSpace(runsRoot))
        {
            return Result<TrainingWorkspacePlan>.Failure("续训输出目录缺少父目录");
        }

        return Result<TrainingWorkspacePlan>.Success(new TrainingWorkspacePlan(
            runsRoot,
            version,
            resolvedOutput,
            resolvedLog,
            Path.Combine(resolvedOutput, "source-voc")));
    }

    private static string SanitizeSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(value.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "run" : cleaned;
    }
}
