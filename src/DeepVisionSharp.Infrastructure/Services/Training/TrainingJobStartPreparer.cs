using MediatR;
using DeepVisionSharp.Application.Galleries;
using DeepVisionSharp.Application.Inference;
using DeepVisionSharp.Application.Models;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Application.Training;
using DeepVisionSharp.Domain.Common;
using DeepVisionSharp.Domain.Entities;
using DeepVisionSharp.Domain.Repositories;
using DeepVisionSharp.Domain.ValueObjects;

namespace DeepVisionSharp.Infrastructure.Services;

internal sealed class TrainingJobStartPreparer
{
    private readonly ITrainingJobRepository _repository;
    private readonly ITrainingProcessHost _processHost;
    private readonly ITrainingWorkspacePlanner _workspacePlanner;
    private readonly TrainingJobStatusWriter _statusWriter;
    private readonly TrainingEnvironmentCheckCoordinator _environmentChecks;
    private readonly IMediator _mediator;
    private readonly IProjectRepository _projects;
    private readonly IInferenceModelCache _modelCache;

    public TrainingJobStartPreparer(
        ITrainingJobRepository repository,
        ITrainingProcessHost processHost,
        ITrainingWorkspacePlanner workspacePlanner,
        TrainingJobStatusWriter statusWriter,
        TrainingEnvironmentCheckCoordinator environmentChecks,
        IMediator mediator,
        IProjectRepository projects,
        IInferenceModelCache modelCache)
    {
        _repository = repository;
        _processHost = processHost;
        _workspacePlanner = workspacePlanner;
        _statusWriter = statusWriter;
        _environmentChecks = environmentChecks;
        _mediator = mediator;
        _projects = projects;
        _modelCache = modelCache;
    }

    public async Task<Result<TrainingStartContext>> PrepareAsync(
        StartTrainingJobCommand request,
        ITrainingBackend backend,
        CancellationToken cancellationToken)
    {
        var job = await _repository.GetByIdAsync(request.JobId, cancellationToken).ConfigureAwait(false);
        if (job is null)
        {
            return Result<TrainingStartContext>.Failure($"训练任务 {request.JobId} 不存在");
        }

        if (request.Resume)
        {
            // Resume is kept in the command for API compatibility but is not a supported product
            // path: ModelSupportsResume is hard-coded false, so the state machine and the resume
            // preparation below are unreachable. This guard is the single place that decides it —
            // the validator, the state machine and this preparer all defer to the same predicate, so
            // flipping one of them alone cannot silently enable a path that has never been tested.
            if (!TrainingJobTransitions.ModelSupportsResume(job.ModelType))
            {
                return Result<TrainingStartContext>.Failure(TrainingValidationMessages.ResumeNotSupported);
            }

            if (job.Status is not (TrainingJobStatus.Failed or TrainingJobStatus.Cancelled))
            {
                return Result<TrainingStartContext>.Failure("仅失败或已取消的任务可续训");
            }

            if (!ModelTypeNames.IsClassificationModelType(job.ModelType))
            {
                return Result<TrainingStartContext>.Failure(TrainingValidationMessages.ResumeNotSupported);
            }
        }
        else if (job.Status != TrainingJobStatus.Draft)
        {
            return Result<TrainingStartContext>.Failure("仅草稿任务可启动");
        }

        var participatingIds = TrainingJobFormMapper.ResolveParticipatingGalleryIds(request.Form);
        if (participatingIds.Count == 0 && request.Resume)
        {
            participatingIds = job.Configuration?.GalleryIds
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToArray()
                ?? [];
        }

        if (participatingIds.Count == 0)
        {
            return Result<TrainingStartContext>.Failure(TrainingValidationMessages.SelectDataset);
        }

        if (job.ProjectId.HasValue && job.ProjectId != request.ProjectId)
        {
            return Result<TrainingStartContext>.Failure("训练任务不属于当前项目");
        }

        if (_processHost.IsActive(request.JobId))
        {
            return Result<TrainingStartContext>.Failure("训练进程已在运行");
        }

        var project = await _projects.GetByIdAsync(request.ProjectId, cancellationToken).ConfigureAwait(false);
        if (project is null)
        {
            return Result<TrainingStartContext>.Failure("训练任务所属项目不存在。");
        }
        ProjectCapabilityCatalog.EnsureActive(project.Type);

        // Summary-only load: start path only needs counts/names, not full sample rows.
        var galleries = new List<ProjectGallery>();
        var annotatedCounts = new Dictionary<Guid, int>();
        foreach (var galleryId in participatingIds)
        {
            var galleryResult = await _mediator.Send(new GetGallerySummaryByIdQuery(galleryId), cancellationToken)
                .ConfigureAwait(false);
            if (galleryResult.IsFailure)
            {
                return Result<TrainingStartContext>.Failure(galleryResult.Error ?? "加载图库失败，请刷新后重试");
            }

            var gallery = galleryResult.Value;
            if (gallery is null)
            {
                return Result<TrainingStartContext>.Failure("所选数据集中包含不存在的图库，请刷新后重试");
            }

            if (gallery.ProjectId != request.ProjectId)
            {
                return Result<TrainingStartContext>.Failure("所选数据集不属于当前项目");
            }

            if (gallery.SampleCount <= 0)
            {
                return Result<TrainingStartContext>.Failure($"所选数据集为空：{gallery.Name}");
            }

            if (gallery.AnnotatedCount <= 0)
            {
                return Result<TrainingStartContext>.Failure($"所选数据集没有标注样本：{gallery.Name}");
            }

            galleries.Add(gallery);
            annotatedCounts[gallery.Id] = gallery.AnnotatedCount;
        }

        var validation = await backend.ValidateAsync(
                new TrainingStartValidationContext(request, participatingIds, galleries, annotatedCounts),
                cancellationToken)
            .ConfigureAwait(false);
        if (validation.IsFailure)
        {
            return Result<TrainingStartContext>.Failure(validation.Error ?? "训练配置无效");
        }

        // Enter Preparing before environment checks / inference stop so the UI can refresh immediately.
        // Heavy environment checks and dataset preparation continue under Preparing.
        Result<TrainingJob> preparing;
        TrainingWorkspacePlan plan;
        if (request.Resume)
        {
            if (string.IsNullOrWhiteSpace(job.OutputRoot) || string.IsNullOrWhiteSpace(job.LogFile))
            {
                return Result<TrainingStartContext>.Failure("续训缺少输出目录或日志路径");
            }

            if (TrainingCheckpointResolver.ResolveLastFromOutputDirectory(job.OutputRoot) is null)
            {
                return Result<TrainingStartContext>.Failure("续训找不到 last.pt，无法从断点继续");
            }

            var reused = _workspacePlanner.ReuseExistingRun(job.OutputRoot, job.LogFile);
            if (reused.IsFailure || reused.Value is null)
            {
                return Result<TrainingStartContext>.Failure(reused.Error ?? "无法复用原训练目录");
            }

            plan = reused.Value;
            preparing = await _statusWriter
                .MarkPreparingForResumeAsync(job, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            plan = _workspacePlanner.CreateRunPlan(request.ProjectId, request.JobId, request.Form.ModelName);
            preparing = await _statusWriter
                .MarkPreparingAsync(
                    job,
                    request.ProjectId,
                    request.Form.ModelName,
                    TrainingJobFormMapper.ToConfiguration(request.Form),
                    plan,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (preparing.IsFailure || preparing.Value is null)
        {
            return Result<TrainingStartContext>.Failure(preparing.Error ?? "无法启动训练任务");
        }

        var preparingJob = preparing.Value;

        try
        {
            var environment = await _environmentChecks
                .CheckAsync(
                    new TrainingEnvironmentCheckContext(request, backend, participatingIds, galleries, annotatedCounts),
                    cancellationToken)
                .ConfigureAwait(false);
            if (!environment.Success)
            {
                await _statusWriter.MarkFailedAsync(preparingJob, cancellationToken).ConfigureAwait(false);
                return Result<TrainingStartContext>.Failure(environment.SummaryMessage);
            }

            var inferenceStatus = await _mediator.Send(new InferenceStatusQuery(), cancellationToken).ConfigureAwait(false);
            if (inferenceStatus.IsSuccess && inferenceStatus.Value)
            {
                await _mediator.Send(new StopInferenceCommand("训练任务启动"), cancellationToken).ConfigureAwait(false);
            }

            // Inference models are resident in this process and share the GPU. StopInference is a
            // no-op for the in-process backend, so release the cached models explicitly; ones
            // currently in use are disposed when their inference returns.
            _modelCache.Clear();

            var informationalMessages = environment.Warnings
                .Select(warning => $"训练环境警告：{warning.Message}")
                .ToList();

            return Result<TrainingStartContext>.Success(new TrainingStartContext(
                request,
                preparingJob,
                plan,
                galleries,
                annotatedCounts,
                participatingIds,
                informationalMessages));
        }
        catch (OperationCanceledException)
        {
            // StopAsync may already mark Cancelled while Preparing; avoid overwriting terminal status.
            var latest = await _repository.GetByIdAsync(request.JobId, CancellationToken.None).ConfigureAwait(false);
            if (latest is not null && latest.Status == TrainingJobStatus.Preparing)
            {
                await _statusWriter.MarkCancelledAsync(latest, CancellationToken.None).ConfigureAwait(false);
            }

            throw;
        }
        catch
        {
            var latest = await _repository.GetByIdAsync(request.JobId, CancellationToken.None).ConfigureAwait(false);
            if (latest is not null && latest.Status == TrainingJobStatus.Preparing)
            {
                await _statusWriter.MarkFailedAsync(latest, CancellationToken.None).ConfigureAwait(false);
            }

            throw;
        }
    }
}
