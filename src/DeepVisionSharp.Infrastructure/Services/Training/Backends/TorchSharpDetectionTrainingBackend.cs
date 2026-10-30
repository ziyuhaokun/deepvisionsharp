using System.Collections.Concurrent;

using Microsoft.Extensions.Logging;

using DeepVisionSharp.Application.Models;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Application.Training;
using DeepVisionSharp.Domain.Common;
using DeepVisionSharp.Domain.Entities;
using DeepVisionSharp.Domain.Repositories;
using DeepVisionSharp.Domain.ValueObjects;
using DeepVisionSharp.TorchSharp.Detection.Training;
using DeepVisionSharp.TorchSharp.Device;

namespace DeepVisionSharp.Infrastructure.Services;

/// <summary>
/// In-process YOLO26 detection training. The orchestration mirrors the classification backend
/// (single-flight, device lease, heartbeat, cancellation, DV_COMPLETED) because both share the
/// same one-GPU runtime.
/// </summary>
internal sealed class TorchSharpDetectionTrainingBackend : ITrainingBackend, IDisposable
{
    private const string RecoveryFailureMessage = "应用重启导致 TorchSharp 检测训练中断，请复制为新任务后重试";
    private const string CancelledMessage = "TorchSharp 检测训练已取消";

    private readonly IDetectionDatasetExportService _datasetExporter;
    private readonly TrainingJobStatusWriter _statusWriter;
    private readonly IProjectRepository _projects;
    private readonly ITrainingJobRepository _repository;
    private readonly ITrainingProcessHost _processHost;
    private readonly IInferenceRuntime _inferenceRuntime;
    private readonly ILogger<TorchSharpDetectionTrainingBackend> _logger;
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _runningTokens = new();
    private readonly SemaphoreSlim _singleFlight = new(1, 1);

    public TorchSharpDetectionTrainingBackend(
        IDetectionDatasetExportService datasetExporter,
        TrainingJobStatusWriter statusWriter,
        IProjectRepository projects,
        ITrainingJobRepository repository,
        ITrainingProcessHost processHost,
        IInferenceRuntime inferenceRuntime,
        ILogger<TorchSharpDetectionTrainingBackend> logger)
    {
        _datasetExporter = datasetExporter;
        _statusWriter = statusWriter;
        _projects = projects;
        _repository = repository;
        _processHost = processHost;
        _inferenceRuntime = inferenceRuntime;
        _logger = logger;
    }

    public string BackendKey => BackendKeys.TorchSharpDetection;

    public bool CanHandle(string? modelType) => ModelTypeNames.IsDetectionModelType(modelType);

    public Task<Result> ValidateAsync(
        TrainingStartValidationContext context,
        CancellationToken cancellationToken)
    {
        var datasetError = TrainingDatasetConfigRules.ValidateForm(
            context.Command.Form,
            context.AnnotatedCounts);
        return Task.FromResult(datasetError is null
            ? Result.Success()
            : Result.Failure(datasetError));
    }

    public async Task<Result<TrainingBackendStartResult>> StartAsync(
        TrainingStartContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var request = context.Command;
        var job = context.Job;
        var plan = context.WorkspacePlan;
        var informationalMessages = context.InformationalMessages;

        void Log(string line)
        {
            _processHost.PublishLog(job.Id, line);
            try
            {
                File.AppendAllText(plan.LogFile, line + Environment.NewLine);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Append TorchSharp DET train log failed for {JobId}", job.Id);
            }
        }

        var project = await _projects.GetByIdAsync(request.ProjectId, cancellationToken).ConfigureAwait(false);
        if (project is null)
        {
            await _statusWriter.MarkFailedAsync(job, cancellationToken).ConfigureAwait(false);
            return Result<TrainingBackendStartResult>.Failure("训练项目不存在");
        }

        if (!ModelTypeNames.TryNormalize(job.ModelType, out var modelType)
            || !ModelTypeNames.IsDetectionModelType(modelType))
        {
            await _statusWriter.MarkFailedAsync(job, cancellationToken).ConfigureAwait(false);
            return Result<TrainingBackendStartResult>.Failure($"不支持的检测模型类型：{job.ModelType}");
        }

        var runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (!_runningTokens.TryAdd(job.Id, runCts))
        {
            runCts.Dispose();
            return Result<TrainingBackendStartResult>.Failure("训练任务已在运行");
        }

        var runToken = runCts.Token;
        try
        {
            if (!await _singleFlight.WaitAsync(0, runToken).ConfigureAwait(false))
            {
                ReleaseRunToken(job.Id, runCts);
                return Result<TrainingBackendStartResult>.Failure(
                    "已有训练任务正在运行，请等待其完成或停止后再启动。");
            }
        }
        catch (OperationCanceledException)
        {
            ReleaseRunToken(job.Id, runCts);
            throw;
        }

        IDisposable trainingGate;
        try
        {
            trainingGate = await _inferenceRuntime
                .BeginExclusiveAsync(TimeSpan.FromSeconds(30), runToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ReleaseRunToken(job.Id, runCts);
            _singleFlight.Release();
            await _statusWriter
                .MarkFailedAsync(job, CancellationToken.None, ex.Message)
                .ConfigureAwait(false);
            return Result<TrainingBackendStartResult>.Failure(ex.Message);
        }

        using var gateLease = trainingGate;

        try
        {
            using var logSession = _processHost.BeginExternalLogSession(job.Id);
            using var heartbeat = new TrainingRunHeartbeat(job.Id, plan.OutputRoot, _logger);

            Log($"--- TorchSharp DET training started at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ---");
            Log($"CUDA available: {TorchSharpDeviceManager.IsCudaAvailable}");

            var running = await _statusWriter
                .MarkRunningAsync(job, processId: 0, runToken)
                .ConfigureAwait(false);
            if (running.IsFailure || running.Value is null)
            {
                await _statusWriter.MarkFailedAsync(job, CancellationToken.None).ConfigureAwait(false);
                return Result<TrainingBackendStartResult>.Failure(running.Error ?? "无法进入运行状态");
            }

            job = running.Value;
            heartbeat.SetStage(TrainingRunMarkerStore.StageExporting);

            var datasetRoot = Path.Combine(plan.OutputRoot, "detection-dataset");
            var roles = request.Form.DatasetSplitMode == TrainingDatasetSplitMode.PurposeAssignment
                ? request.Form.GalleryRoles
                : new Dictionary<Guid, GalleryDatasetRole>();
            var classes = project.Labels
                .Where(label => !string.IsNullOrWhiteSpace(label))
                .Select(label => label.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var export = await _datasetExporter.ExportAsync(
                request.ProjectId,
                context.ParticipatingGalleryIds,
                roles,
                request.Form.DatasetSplitMode,
                request.Form.ValidationRatio,
                classes,
                datasetRoot,
                runToken).ConfigureAwait(false);
            if (export.IsFailure || export.Value is null)
            {
                await _statusWriter
                    .MarkFailedAsync(job, CancellationToken.None, export.Error ?? "检测数据集导出失败")
                    .ConfigureAwait(false);
                return Result<TrainingBackendStartResult>.Failure(export.Error ?? "检测数据集导出失败");
            }

            Log(
                $"检测数据集完成：train={export.Value.TrainingCount}，val={export.Value.ValidationCount}，" +
                $"框={export.Value.TrainingBoxes + export.Value.ValidationBoxes}，" +
                $"未标注跳过={export.Value.SkippedUnreviewed}，未知标签框跳过={export.Value.SkippedBoxes}");
            informationalMessages.Add(
                $"检测数据集完成：train={export.Value.TrainingCount}，val={export.Value.ValidationCount}");

            if (request.Form.BatchSize > export.Value.TrainingCount)
            {
                var msg = $"Batch Size ({request.Form.BatchSize}) 不能超过训练样本数 ({export.Value.TrainingCount})";
                await _statusWriter.MarkFailedAsync(job, CancellationToken.None, msg).ConfigureAwait(false);
                return Result<TrainingBackendStartResult>.Failure(msg);
            }

            var withDataRoot = await _statusWriter
                .MarkRunningAsync(job, processId: 0, runToken, dataRoot: datasetRoot)
                .ConfigureAwait(false);
            if (withDataRoot.IsSuccess && withDataRoot.Value is not null)
            {
                job = withDataRoot.Value;
            }

            var inputWidth = Math.Max(32, request.Form.NetworkInputWidth);
            var inputHeight = Math.Max(32, request.Form.NetworkInputHeight);
            var detectionOptions = request.Form.ToDetectionOptions() ?? DetectionTrainingOptions.Default;

            TorchSharpDetTrainer.Result trainResult;
            heartbeat.SetStage(TrainingRunMarkerStore.StageTraining);
            try
            {
                trainResult = await Task.Run(
                    () => TorchSharpDetTrainer.Run(
                        new TorchSharpDetTrainer.Options(
                            DatasetRoot: datasetRoot,
                            OutputRoot: plan.OutputRoot,
                            ModelType: modelType,
                            Epochs: Math.Max(1, request.Form.Epochs),
                            BatchSize: Math.Max(1, request.Form.BatchSize),
                            LearningRate: (float)request.Form.Lr,
                            Optimizer: string.IsNullOrWhiteSpace(request.Form.Optimizer)
                                ? "sgd"
                                : request.Form.Optimizer,
                            InputWidth: inputWidth,
                            InputHeight: inputHeight,
                            InputChannels: 3,
                            DeviceName: "AUTO",
                            HorizontalFlip: detectionOptions.HorizontalFlip,
                            EarlyStoppingPatience: Math.Max(0, request.Form.EarlyStoppingPatience),
                            EvaluationConfidence: (float)detectionOptions.EvaluationConfidence,
                            NmsIou: (float)detectionOptions.NmsIouThreshold,
                            MaxDetections: detectionOptions.MaxDetections,
                            Log: Log,
                            Progress: (current, total, message) =>
                            {
                                heartbeat.ReportProgress(current, total, message);
                                var percent = total <= 0
                                    ? 0
                                    : Math.Clamp((int)Math.Round(current * 100.0 / total), 0, 100);
                                Log(
                                    $"DV_PROGRESS {{\"stage\":\"training\",\"current\":{current},\"total\":{total},\"percent\":{percent},\"message\":{System.Text.Json.JsonSerializer.Serialize(message)}}}");
                            },
                            PreviewGenerated: path => _processHost.PublishPreviewUpdated(job.Id, path)),
                        runToken),
                    runToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (runToken.IsCancellationRequested)
            {
                Log(CancelledMessage);
                var latest = await _repository.GetByIdAsync(job.Id, CancellationToken.None).ConfigureAwait(false)
                    ?? job;
                await _statusWriter
                    .CompleteAsync(
                        latest,
                        TrainingJobStatus.Cancelled,
                        bestModelPath: File.Exists(Path.Combine(plan.OutputRoot, TorchSharpDetTrainer.BestFileName))
                            ? Path.Combine(plan.OutputRoot, TorchSharpDetTrainer.BestFileName)
                            : null,
                        cancellationToken: CancellationToken.None,
                        failureMessage: CancelledMessage)
                    .ConfigureAwait(false);
                return Result<TrainingBackendStartResult>.Failure(CancelledMessage);
            }

            if (runToken.IsCancellationRequested)
            {
                Log(CancelledMessage);
                var cancelledJob = await _repository.GetByIdAsync(job.Id, CancellationToken.None).ConfigureAwait(false)
                    ?? job;
                await _statusWriter
                    .CompleteAsync(
                        cancelledJob,
                        TrainingJobStatus.Cancelled,
                        bestModelPath: File.Exists(trainResult.BestModelPath) ? trainResult.BestModelPath : null,
                        cancellationToken: CancellationToken.None,
                        failureMessage: CancelledMessage)
                    .ConfigureAwait(false);
                return Result<TrainingBackendStartResult>.Failure(CancelledMessage);
            }

            Log($"DV_COMPLETED {{\"best_model_path\":{System.Text.Json.JsonSerializer.Serialize(trainResult.BestModelPath)},\"metadata_path\":{System.Text.Json.JsonSerializer.Serialize(trainResult.MetadataPath)},\"task\":\"detect\"}}");
            heartbeat.SetStage(TrainingRunMarkerStore.StageSaving);

            var latestForComplete = await _repository.GetByIdAsync(job.Id, CancellationToken.None).ConfigureAwait(false)
                ?? job;
            var completed = await _statusWriter
                .CompleteAsync(
                    latestForComplete,
                    TrainingJobStatus.Succeeded,
                    trainResult.BestModelPath,
                    cancellationToken: CancellationToken.None)
                .ConfigureAwait(false);
            if (completed.IsFailure || completed.Value is null)
            {
                await _statusWriter
                    .MarkFailedAsync(job, CancellationToken.None, completed.Error)
                    .ConfigureAwait(false);
                return Result<TrainingBackendStartResult>.Failure(completed.Error ?? "无法完成任务状态更新");
            }

            informationalMessages.Add(
                $"TorchSharp 检测训练完成：mAP50={trainResult.BestMap50:F4}，" +
                $"mAP50-95={trainResult.BestMap50_95:F4}，耗时={trainResult.ElapsedMilliseconds:F0}ms");

            return Result<TrainingBackendStartResult>.Success(new TrainingBackendStartResult(
                completed.Value,
                modelType,
                datasetRoot,
                plan.OutputRoot,
                plan.LogFile,
                informationalMessages));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "TorchSharp DET training failed for job {JobId}", job.Id);
            Log($"[ERR] TorchSharp DET training failed: {ex}");
            await _statusWriter
                .MarkFailedAsync(job, CancellationToken.None, ex.Message)
                .ConfigureAwait(false);
            return Result<TrainingBackendStartResult>.Failure(ex.Message);
        }
        finally
        {
            ReleaseRunToken(job.Id, runCts);
            _singleFlight.Release();
            TorchDeviceMemory.EmptyCache(_logger);
        }
    }

    private void ReleaseRunToken(Guid jobId, CancellationTokenSource runCts)
    {
        if (_runningTokens.TryRemove(jobId, out var registered) && ReferenceEquals(registered, runCts))
        {
            registered.Dispose();
        }
        else
        {
            runCts.Dispose();
        }
    }

    public Task<Result> StopAsync(TrainingJob job, string? reason, CancellationToken cancellationToken)
    {
        if (_runningTokens.TryGetValue(job.Id, out var runCts))
        {
            try
            {
                runCts.Cancel();
                _logger.LogInformation(
                    "Requested cancel for TorchSharp DET training job {JobId}. Reason={Reason}",
                    job.Id,
                    reason ?? "unspecified");
                return Task.FromResult(Result.Success());
            }
            catch (ObjectDisposedException)
            {
            }
        }

        return Task.FromResult(Result.Success());
    }

    public Task<TrainingRecoveryDecision> EvaluateRecoveryAsync(TrainingJob job, CancellationToken cancellationToken)
    {
        if (_runningTokens.ContainsKey(job.Id) || _processHost.IsActive(job.Id))
        {
            return Task.FromResult(new TrainingRecoveryDecision(TrainingRecoveryAction.KeepRunning));
        }

        var marker = TrainingRunMarkerStore.TryRead(job.OutputRoot, _logger);
        if (TrainingJobRecoveryEvaluator.CanRecoverAsSucceeded(job, out var bestModelPath, out var lastModelPath))
        {
            TrainingRunMarkerStore.Delete(job.OutputRoot, _logger);
            return Task.FromResult(new TrainingRecoveryDecision(
                TrainingRecoveryAction.MarkSucceeded,
                BestModelPath: bestModelPath,
                LastModelPath: lastModelPath));
        }

        TrainingRunMarkerStore.Delete(job.OutputRoot, _logger);
        return Task.FromResult(new TrainingRecoveryDecision(
            TrainingRecoveryAction.MarkFailed,
            Reason: TrainingRunMarkerStore.BuildInterruptionMessage(marker, job.Id, RecoveryFailureMessage)));
    }

    public void Dispose()
    {
        foreach (var pair in _runningTokens.ToArray())
        {
            if (_runningTokens.TryRemove(pair.Key, out var cts))
            {
                try
                {
                    cts.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }
                finally
                {
                    cts.Dispose();
                }
            }
        }

        _singleFlight.Dispose();
    }
}
