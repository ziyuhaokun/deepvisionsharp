using DeepVisionSharp.Domain.Common;
using DeepVisionSharp.Domain.ValueObjects;

namespace DeepVisionSharp.Domain.Entities;

public enum TrainingJobStatus
{
    Draft,
    Preparing,
    Running,
    Succeeded,
    Failed,
    Cancelled
}

public static class TrainingJobTransitions
{
    public static bool IsTerminal(TrainingJobStatus status)
        => status is TrainingJobStatus.Succeeded
            or TrainingJobStatus.Failed
            or TrainingJobStatus.Cancelled;

    public static bool IsActive(TrainingJobStatus status)
        => status is TrainingJobStatus.Preparing or TrainingJobStatus.Running;

    public static bool CanTransition(TrainingJobStatus from, TrainingJobStatus to)
        => CanTransition(from, to, modelType: null);

    public static bool CanTransition(TrainingJobStatus from, TrainingJobStatus to, string? modelType)
    {
        if (from == to)
        {
            return true;
        }

        return (from, to) switch
        {
            (TrainingJobStatus.Draft, TrainingJobStatus.Preparing) => true,
            (TrainingJobStatus.Draft, TrainingJobStatus.Failed) => true,
            (TrainingJobStatus.Preparing, TrainingJobStatus.Running) => true,
            (TrainingJobStatus.Preparing, TrainingJobStatus.Failed) => true,
            (TrainingJobStatus.Preparing, TrainingJobStatus.Cancelled) => true,
            (TrainingJobStatus.Running, TrainingJobStatus.Succeeded) => true,
            (TrainingJobStatus.Running, TrainingJobStatus.Failed) => true,
            (TrainingJobStatus.Running, TrainingJobStatus.Cancelled) => true,
            // 续训在 1.0 分类任务中不可用。
            (TrainingJobStatus.Failed, TrainingJobStatus.Preparing) => ModelSupportsResume(modelType),
            (TrainingJobStatus.Cancelled, TrainingJobStatus.Preparing) => ModelSupportsResume(modelType),
            _ => false
        };
    }

    /// <summary>Resume is not supported by the classification product.</summary>
    public static bool ModelSupportsResume(string? modelType) => false;
}

public sealed class TrainingJob
{
    public TrainingJob(
        Guid id,
        Guid? projectId,
        string name,
        string backend,
        string modelType,
        string? dataRoot,
        string? outputRoot,
        TrainingJobStatus status,
        DateTime? startedAt,
        DateTime? finishedAt,
        string? bestModelPath,
        string? lastModelPath,
        string? logFile,
        int? processId,
        TrainingConfiguration? configuration,
        string? failureMessage)
    {
        Id = id;
        ProjectId = projectId;
        Name = name;
        Backend = backend;
        ModelType = modelType;
        DataRoot = dataRoot;
        OutputRoot = outputRoot;
        Status = status;
        StartedAt = startedAt;
        FinishedAt = finishedAt;
        BestModelPath = bestModelPath;
        LastModelPath = lastModelPath;
        LogFile = logFile;
        ProcessId = processId;
        Configuration = configuration;
        FailureMessage = failureMessage;
    }

    public Guid Id { get; }

    public Guid? ProjectId { get; }

    public string Name { get; }

    public string Backend { get; }

    public string ModelType { get; }

    public string? DataRoot { get; }

    public string? OutputRoot { get; }

    public TrainingJobStatus Status { get; }

    public DateTime? StartedAt { get; }

    public DateTime? FinishedAt { get; }

    public string? BestModelPath { get; }

    public string? LastModelPath { get; }

    public string? LogFile { get; }

    public int? ProcessId { get; }

    public TrainingConfiguration? Configuration { get; }

    public string? FailureMessage { get; }

    public static TrainingJob CreateDraft(string name, Guid? projectId = null)
    {
        return new TrainingJob(
            Guid.NewGuid(),
            projectId,
            name.Trim(),
            "torchsharp:classification-train",
            "resnet18",
            dataRoot: null,
            outputRoot: null,
            TrainingJobStatus.Draft,
            startedAt: null,
            finishedAt: null,
            bestModelPath: null,
            lastModelPath: null,
            logFile: null,
            processId: null,
            configuration: null,
            failureMessage: null);
    }

    public static TrainingJob FromPersistence(
        Guid id,
        Guid? projectId,
        string name,
        string backend,
        string modelType,
        string? dataRoot,
        string? outputRoot,
        TrainingJobStatus status,
        DateTime? startedAt,
        DateTime? finishedAt,
        string? bestModelPath,
        string? lastModelPath,
        string? logFile,
        int? processId,
        TrainingConfiguration? configuration,
        string? failureMessage)
    {
        return new TrainingJob(
            id,
            projectId,
            name,
            backend,
            modelType,
            dataRoot,
            outputRoot,
            status,
            startedAt,
            finishedAt,
            bestModelPath,
            lastModelPath,
            logFile,
            processId,
            configuration,
            failureMessage);
    }

    public Result<TrainingJob> ConfigureDraftModel(string backend, string modelType)
    {
        if (Status != TrainingJobStatus.Draft)
        {
            return Result<TrainingJob>.Failure("仅草稿任务可配置模型类型");
        }

        return Result<TrainingJob>.Success(With(backend: backend, modelType: modelType));
    }

    public Result<TrainingJob> CopySettingsFrom(TrainingJob source)
    {
        if (Status != TrainingJobStatus.Draft)
        {
            return Result<TrainingJob>.Failure("仅草稿任务可复制配置");
        }

        return Result<TrainingJob>.Success(With(
            backend: source.Backend,
            modelType: source.ModelType,
            configuration: source.Configuration));
    }

    public Result<TrainingJob> RenameDraft(string name)
    {
        if (Status != TrainingJobStatus.Draft)
        {
            return Result<TrainingJob>.Failure("任务已开始，无法修改名称");
        }

        return Result<TrainingJob>.Success(With(name: name.Trim()));
    }

    public Result<TrainingJob> UpdateDraft(string name, TrainingConfiguration configuration)
    {
        if (Status != TrainingJobStatus.Draft)
        {
            return Result<TrainingJob>.Failure("仅草稿任务可更新配置");
        }

        return Result<TrainingJob>.Success(With(name: name.Trim(), configuration: configuration));
    }

    public Result<TrainingJob> PrepareForStart(
        Guid projectId,
        string name,
        TrainingConfiguration configuration,
        string dataRoot,
        string outputRoot,
        string logFile)
    {
        if (Status != TrainingJobStatus.Draft)
        {
            return Result<TrainingJob>.Failure("仅草稿任务可启动");
        }

        if (!TrainingJobTransitions.CanTransition(Status, TrainingJobStatus.Preparing))
        {
            return Result<TrainingJob>.Failure($"训练任务状态无法从 {Status} 转换到 {TrainingJobStatus.Preparing}");
        }

        return Result<TrainingJob>.Success(With(
            status: TrainingJobStatus.Preparing,
            projectId: projectId,
            name: name.Trim(),
            configuration: configuration,
            dataRoot: dataRoot,
            outputRoot: outputRoot,
            logFile: logFile,
            resetStartedAt: true,
            resetFinishedAt: true,
            resetProcessId: true,
            resetBestModelPath: true,
            resetLastModelPath: true,
            resetFailureMessage: true));
    }

    /// <summary>
    /// 从失败或取消状态进入续训准备：复用原 OutputRoot / DataRoot / 日志与 checkpoint 路径。
    /// </summary>
    public Result<TrainingJob> PrepareForResume()
    {
        if (Status is not (TrainingJobStatus.Failed or TrainingJobStatus.Cancelled))
        {
            return Result<TrainingJob>.Failure("仅失败或已取消的任务可续训");
        }

        if (!ProjectId.HasValue || ProjectId.Value == Guid.Empty)
        {
            return Result<TrainingJob>.Failure("续训缺少项目");
        }

        if (string.IsNullOrWhiteSpace(OutputRoot))
        {
            return Result<TrainingJob>.Failure("续训缺少输出目录");
        }

        if (string.IsNullOrWhiteSpace(LogFile))
        {
            return Result<TrainingJob>.Failure("续训缺少日志路径");
        }

        if (!TrainingJobTransitions.ModelSupportsResume(ModelType))
        {
            return Result<TrainingJob>.Failure("当前产品不支持训练续训");
        }

        if (!TrainingJobTransitions.CanTransition(Status, TrainingJobStatus.Preparing, ModelType))
        {
            return Result<TrainingJob>.Failure($"训练任务状态无法从 {Status} 转换到 {TrainingJobStatus.Preparing}");
        }

        return Result<TrainingJob>.Success(With(
            status: TrainingJobStatus.Preparing,
            resetStartedAt: true,
            resetFinishedAt: true,
            resetProcessId: true,
            resetFailureMessage: true));
    }

    public Result<TrainingJob> MarkRunning(int processId, DateTime startedAtUtc, string? dataRoot = null)
    {
        if (!TrainingJobTransitions.CanTransition(Status, TrainingJobStatus.Running))
        {
            return Result<TrainingJob>.Failure($"训练任务状态无法从 {Status} 转换到 {TrainingJobStatus.Running}");
        }

        return Result<TrainingJob>.Success(With(
            status: TrainingJobStatus.Running,
            startedAt: startedAtUtc,
            processId: processId,
            dataRoot: dataRoot));
    }

    public Result<TrainingJob> Complete(
        TrainingJobStatus terminalStatus,
        DateTime finishedAtUtc,
        string? bestModelPath = null,
        string? lastModelPath = null,
        bool captureCheckpointPaths = false,
        string? failureMessage = null)
    {
        if (!TrainingJobTransitions.IsTerminal(terminalStatus))
        {
            return Result<TrainingJob>.Failure("仅可转换到终态");
        }

        if (!TrainingJobTransitions.CanTransition(Status, terminalStatus))
        {
            return Result<TrainingJob>.Failure($"训练任务状态无法从 {Status} 转换到 {terminalStatus}");
        }

        if (captureCheckpointPaths)
        {
            return Result<TrainingJob>.Success(With(
                status: terminalStatus,
                finishedAt: finishedAtUtc,
                resetProcessId: true,
                bestModelPath: string.IsNullOrWhiteSpace(bestModelPath) ? null : bestModelPath,
                resetBestModelPath: true,
                lastModelPath: string.IsNullOrWhiteSpace(lastModelPath) ? null : lastModelPath,
                resetLastModelPath: true,
                failureMessage: terminalStatus == TrainingJobStatus.Failed ? failureMessage?.Trim() : null,
                resetFailureMessage: true));
        }

        var applyBest = bestModelPath is not null;
        var applyLast = lastModelPath is not null;

        return Result<TrainingJob>.Success(With(
            status: terminalStatus,
            finishedAt: finishedAtUtc,
            resetProcessId: true,
            bestModelPath: applyBest
                ? (string.IsNullOrWhiteSpace(bestModelPath) ? null : bestModelPath)
                : BestModelPath,
            resetBestModelPath: applyBest && string.IsNullOrWhiteSpace(bestModelPath),
            lastModelPath: applyLast
                ? (string.IsNullOrWhiteSpace(lastModelPath) ? null : lastModelPath)
                : LastModelPath,
            resetLastModelPath: applyLast && string.IsNullOrWhiteSpace(lastModelPath),
            failureMessage: terminalStatus == TrainingJobStatus.Failed ? failureMessage?.Trim() : null,
            resetFailureMessage: true));
    }

    public Result<TrainingJob> MarkFailed(DateTime finishedAtUtc, string? failureMessage = null) =>
        Complete(TrainingJobStatus.Failed, finishedAtUtc, failureMessage: failureMessage);

    public Result<TrainingJob> MarkCancelled(DateTime finishedAtUtc) =>
        Complete(TrainingJobStatus.Cancelled, finishedAtUtc);

    private TrainingJob With(
        Guid? projectId = null,
        string? name = null,
        string? backend = null,
        string? modelType = null,
        string? dataRoot = null,
        string? outputRoot = null,
        TrainingJobStatus? status = null,
        DateTime? startedAt = null,
        bool resetStartedAt = false,
        DateTime? finishedAt = null,
        bool resetFinishedAt = false,
        string? bestModelPath = null,
        bool resetBestModelPath = false,
        string? lastModelPath = null,
        bool resetLastModelPath = false,
        string? logFile = null,
        int? processId = null,
        bool resetProcessId = false,
        TrainingConfiguration? configuration = null,
        string? failureMessage = null,
        bool resetFailureMessage = false)
    {
        return new TrainingJob(
            Id,
            projectId ?? ProjectId,
            name ?? Name,
            backend ?? Backend,
            modelType ?? ModelType,
            dataRoot ?? DataRoot,
            outputRoot ?? OutputRoot,
            status ?? Status,
            resetStartedAt ? startedAt : startedAt ?? StartedAt,
            resetFinishedAt ? finishedAt : finishedAt ?? FinishedAt,
            resetBestModelPath ? bestModelPath : bestModelPath ?? BestModelPath,
            resetLastModelPath ? lastModelPath : lastModelPath ?? LastModelPath,
            logFile ?? LogFile,
            resetProcessId ? processId : processId ?? ProcessId,
            configuration ?? Configuration,
            resetFailureMessage ? failureMessage : failureMessage ?? FailureMessage);
    }
}
