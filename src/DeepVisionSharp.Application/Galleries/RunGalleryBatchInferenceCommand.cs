using DeepVisionSharp.Application.Common;
using DeepVisionSharp.Domain.Common;
using DeepVisionSharp.Domain.Repositories;
using DeepVisionSharp.Domain.ValueObjects;
using DeepVisionSharp.Application.Inference;
using DeepVisionSharp.Application.Models;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Application.Projects;
using MediatR;
using Microsoft.Extensions.Logging;

namespace DeepVisionSharp.Application.Galleries;

public enum GalleryBatchInferenceScope
{
    All,
    UnlabeledOnly,
    LabeledOnly
}

public enum GalleryBatchInferenceWriteBackPolicy
{
    /// <summary>Skip samples that already have a class; write the prediction to the rest.</summary>
    SkipExisting,
    /// <summary>
    /// Append model detections to existing annotations. Whole-image classification stores exactly one
    /// class per sample, so appending has nothing to add and behaves like <see cref="Replace"/>.
    /// </summary>
    Append,
    /// <summary>Overwrite the existing image class.</summary>
    Replace
}

public sealed record GalleryBatchInferenceProgress(
    string Stage,
    int Processed,
    int Total,
    string? CurrentFile,
    int Written,
    int Skipped,
    int Failed);

public sealed record GalleryBatchInferenceResult(
    int Total,
    int Written,
    int Skipped,
    int Failed,
    IReadOnlyList<string> Errors);

public sealed record RunGalleryBatchInferenceCommand(
    Guid ProjectId,
    Guid GalleryId,
    Guid ModelId,
    string ModelPath,
    string ModelType,
    InferenceWriteBackKind WriteBackKind,
    GalleryBatchInferenceScope Scope,
    GalleryBatchInferenceWriteBackPolicy WriteBackPolicy,
    double ConfidenceThreshold,
    string Device,
    int MaxDetections,
    IProgress<GalleryBatchInferenceProgress>? Progress = null,
    string? OwnerKey = null) : ICommand<GalleryBatchInferenceResult>;

public sealed class RunGalleryBatchInferenceCommandHandler
    : ICommandHandler<RunGalleryBatchInferenceCommand, GalleryBatchInferenceResult>
{
    private readonly IMediator _mediator;
    private readonly IGalleryRepository _galleries;
    private readonly IProjectRepository _projects;
    private readonly IProjectLabelService _labels;
    private readonly IAnnotationWorkflowService _annotations;
    private readonly IInferenceRunner _inferenceRunner;
    private readonly IInferenceInputFileProbe _fileProbe;
    private readonly ILogger<RunGalleryBatchInferenceCommandHandler> _logger;

    public RunGalleryBatchInferenceCommandHandler(
        IMediator mediator,
        IGalleryRepository galleries,
        IProjectRepository projects,
        IProjectLabelService labels,
        IAnnotationWorkflowService annotations,
        IInferenceRunner inferenceRunner,
        IInferenceInputFileProbe fileProbe,
        ILogger<RunGalleryBatchInferenceCommandHandler> logger)
    {
        _mediator = mediator;
        _galleries = galleries;
        _projects = projects;
        _labels = labels;
        _annotations = annotations;
        _inferenceRunner = inferenceRunner;
        _fileProbe = fileProbe;
        _logger = logger;
    }

    public async Task<Result<GalleryBatchInferenceResult>> Handle(
        RunGalleryBatchInferenceCommand request,
        CancellationToken cancellationToken)
    {
        var project = await _projects.GetByIdAsync(request.ProjectId, cancellationToken);
        if (project is null)
        {
            return Result<GalleryBatchInferenceResult>.Failure("项目不存在。");
        }
        ProjectCapabilityCatalog.EnsureActive(project.Type);
        var gallery = await _galleries.GetSummaryByIdAsync(request.GalleryId, cancellationToken);
        if (gallery is null || gallery.ProjectId != project.Id)
        {
            return Result<GalleryBatchInferenceResult>.Failure("图库不属于当前项目。");
        }

        var deviceResult = InferenceMaxDetectionsLimits.NormalizeDevice(request.Device);
        if (deviceResult.IsFailure || string.IsNullOrWhiteSpace(deviceResult.Value))
        {
            return Result<GalleryBatchInferenceResult>.Failure(
                deviceResult.Error ?? "推理设备无效。");
        }

        var device = deviceResult.Value;

        if (request.WriteBackKind is InferenceWriteBackKind.None)
        {
            return Result<GalleryBatchInferenceResult>.Failure("当前模型不支持图库批量推理写回。");
        }

        request.Progress?.Report(new GalleryBatchInferenceProgress(
            "正在加载标签与样本列表…", 0, 0, null, 0, 0, 0));

        var labelOptions = await _labels.ListAsync(request.ProjectId, cancellationToken);
        if (labelOptions.Count == 0)
        {
            return Result<GalleryBatchInferenceResult>.Failure("项目尚未配置类别，无法写回推理结果。");
        }

        var labelByName = DetectionInferenceWriteBackMapper.BuildLabelLookup(labelOptions);

        request.Progress?.Report(new GalleryBatchInferenceProgress(
            "正在筛选待推理样本…", 0, 0, null, 0, 0, 0));

        GallerySampleFilter? filter = request.Scope switch
        {
            GalleryBatchInferenceScope.UnlabeledOnly => new GallerySampleFilter { IsAnnotated = false },
            GalleryBatchInferenceScope.LabeledOnly => new GallerySampleFilter { IsAnnotated = true },
            _ => null
        };

        var fileNames = await _galleries.ListSampleFileNamesAsync(
            request.GalleryId,
            filter,
            cancellationToken);
        if (fileNames.Count == 0)
        {
            return Result<GalleryBatchInferenceResult>.Success(
                new GalleryBatchInferenceResult(0, 0, 0, 0, Array.Empty<string>()));
        }

        if (!_fileProbe.ModelExists(request.ModelPath))
        {
            return Result<GalleryBatchInferenceResult>.Failure("模型文件不存在。");
        }

        if (!ModelTypeNames.TryNormalize(request.ModelType, out var normalizedModelType))
        {
            return Result<GalleryBatchInferenceResult>.Failure($"不支持的推理模型类型：{request.ModelType}");
        }

        var config = new InferenceConfig
        {
            ModelId = request.ModelId,
            ConfidenceThreshold = request.ConfidenceThreshold,
            MaxDetections = request.MaxDetections,
            BatchSize = 8,
            Device = device,
            OwnerKey = request.OwnerKey
        };

        const int BatchPersistSize = 50;
        var batchBuffer = new List<AnnotationSaveItem>(BatchPersistSize);
        var written = 0;
        var skipped = 0;
        var failed = 0;
        var errors = new List<string>();

        // The first sample usually pays runtime and model-load cost; surface that before the loop.
        request.Progress?.Report(new GalleryBatchInferenceProgress(
            "正在启动推理引擎并加载模型（首次较慢）…",
            0,
            fileNames.Count,
            fileNames[0],
            0,
            0,
            0));

        for (var index = 0; index < fileNames.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileName = fileNames[index];
            var stage = index == 0
                ? "首张推理中（加载模型后会加快）…"
                : "逐张推理中…";
            request.Progress?.Report(new GalleryBatchInferenceProgress(
                stage,
                index,
                fileNames.Count,
                fileName,
                written,
                skipped,
                failed));

            try
            {
                var processed = await ProcessSampleAsync(
                    request,
                    config,
                    normalizedModelType,
                    labelByName,
                    fileName,
                    cancellationToken);
                switch (processed.Outcome)
                {
                    case SampleOutcome.Written when processed.ItemToSave is not null:
                        batchBuffer.Add(processed.ItemToSave);
                        if (batchBuffer.Count >= BatchPersistSize)
                        {
                            await FlushBatchAsync(batchBuffer, cancellationToken).ConfigureAwait(false);
                        }
                        break;
                    case SampleOutcome.Skipped:
                        skipped++;
                        break;
                    default:
                        failed++;
                        if (processed.Error is not null && errors.Count < 20)
                        {
                            errors.Add(processed.Error);
                        }

                        break;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                failed++;
                if (errors.Count < 20)
                {
                    errors.Add($"{fileName}: {ex.Message}");
                }

                _logger.LogWarning(
                    ex,
                    "Gallery batch inference failed for gallery {GalleryId}, file {FileName}",
                    request.GalleryId,
                    fileName);
            }
        }

        if (batchBuffer.Count > 0)
        {
            await FlushBatchAsync(batchBuffer, cancellationToken).ConfigureAwait(false);
        }

        async Task FlushBatchAsync(List<AnnotationSaveItem> buffer, CancellationToken token)
        {
            try
            {
                await _annotations.SaveBatchAsync(request.GalleryId, buffer, token).ConfigureAwait(false);
                written += buffer.Count;
            }
            catch (Exception ex)
            {
                failed += buffer.Count;
                if (errors.Count < 20)
                {
                    errors.Add($"批量保存标注异常：{ex.Message}");
                }
                _logger.LogError(ex, "Failed to batch save {Count} annotations for gallery {GalleryId}", buffer.Count, request.GalleryId);
            }
            finally
            {
                buffer.Clear();
            }
        }

        request.Progress?.Report(new GalleryBatchInferenceProgress(
            "完成",
            fileNames.Count,
            fileNames.Count,
            null,
            written,
            skipped,
            failed));

        return Result<GalleryBatchInferenceResult>.Success(
            new GalleryBatchInferenceResult(fileNames.Count, written, skipped, failed, errors));
    }

    private async Task<SampleProcessResult> ProcessSampleAsync(
        RunGalleryBatchInferenceCommand request,
        InferenceConfig config,
        string normalizedModelType,
        IReadOnlyDictionary<string, ProjectLabelOption> labelByName,
        string fileName,
        CancellationToken cancellationToken)
    {
        // Channel B: this is a per-sample hot path (once per image in the gallery), so it talks to
        // the workflow port and inference runner directly instead of round-tripping MediatR requests
        // and pipeline behavior stacks for every image.
        var existing = await _annotations.LoadAsync(request.GalleryId, fileName, cancellationToken)
            .ConfigureAwait(false);
        if (request.WriteBackPolicy == GalleryBatchInferenceWriteBackPolicy.SkipExisting
            && !string.IsNullOrWhiteSpace(existing.ImageClass))
        {
            return new SampleProcessResult(SampleOutcome.Skipped, null);
        }

        var imagePath = _annotations.GetImagePath(request.ProjectId, request.GalleryId, fileName);
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return new SampleProcessResult(SampleOutcome.Failed, $"{fileName}: 样本图片路径未找到");
        }

        var inference = await _inferenceRunner.RunSingleAsync(
            request.ModelPath,
            imagePath,
            config,
            normalizedModelType,
            cancellationToken).ConfigureAwait(false);

        if (!inference.Success)
        {
            var err = inference.Error ?? "推理失败";
            return new SampleProcessResult(SampleOutcome.Failed, $"{fileName}: {err}");
        }

        var merge = GalleryBatchInferenceAnnotationMerger.TryMerge(
            request.WriteBackKind,
            request.WriteBackPolicy,
            existing,
            inference,
            fileName,
            labelByName);
        if (merge.Outcome != SampleOutcome.Written || merge.Document is null)
        {
            return new SampleProcessResult(merge.Outcome, merge.Error);
        }

        return new SampleProcessResult(
            SampleOutcome.Written,
            null,
            new AnnotationSaveItem(
                fileName,
                merge.Document.ImageWidth,
                merge.Document.ImageHeight,
                merge.Document.ImageClass));
    }

    private readonly record struct SampleProcessResult(
        SampleOutcome Outcome,
        string? Error,
        AnnotationSaveItem? ItemToSave = null);

    private enum SampleOutcome
    {
        Written,
        Skipped,
        Failed
    }

    private static class GalleryBatchInferenceAnnotationMerger
    {
        public static MergeResult TryMerge(
            InferenceWriteBackKind writeBackKind,
            GalleryBatchInferenceWriteBackPolicy policy,
            AnnotationDocument existing,
            InferenceResult inference,
            string fileName,
            IReadOnlyDictionary<string, ProjectLabelOption> labelByName)
        {
            return writeBackKind switch
            {
                InferenceWriteBackKind.ImageClassTop1 =>
                    MergeImageClass(policy, existing, inference, fileName, labelByName),
                _ => new MergeResult(SampleOutcome.Failed, null)
            };
        }

        private static MergeResult MergeImageClass(
            GalleryBatchInferenceWriteBackPolicy policy,
            AnnotationDocument existing,
            InferenceResult inference,
            string fileName,
            IReadOnlyDictionary<string, ProjectLabelOption> labelByName)
        {
            var prediction = inference.Detections
                .OrderByDescending(item => item.Confidence)
                .FirstOrDefault();
            if (prediction is null)
            {
                return new MergeResult(SampleOutcome.Skipped, null);
            }

            var name = string.IsNullOrWhiteSpace(prediction.ClassName) ? prediction.Label : prediction.ClassName;
            if (string.IsNullOrWhiteSpace(name) || !labelByName.ContainsKey(name.Trim()))
            {
                return new MergeResult(SampleOutcome.Failed, null);
            }

            // Whole-image classification holds one class per sample, so there is nothing to append to:
            // writing the model class is a replace either way. Only SkipExisting differs, and that is
            // decided by the caller before inference runs.
            _ = policy;

            // An unannotated sample has no stored dimensions (LoadAnnotations returns 0x0), so carrying
            // `existing` over would persist 0x0 into the sidecar and the annotation row. Prefer the
            // size the backend actually decoded; fall back to the existing values for older backends.
            var width = inference.ImageWidth > 0 ? inference.ImageWidth : existing.ImageWidth;
            var height = inference.ImageHeight > 0 ? inference.ImageHeight : existing.ImageHeight;
            if (width <= 0 || height <= 0)
            {
                // Never write a geometry-less annotation: it would be unreadable and propagate through
                // project export/import with no repair path.
                return new MergeResult(
                    SampleOutcome.Failed,
                    null,
                    $"{fileName}: 推理结果缺少图像尺寸，已跳过写回");
            }

            return new MergeResult(
                SampleOutcome.Written,
                new AnnotationDocument(width, height, name.Trim()));
        }

        internal readonly record struct MergeResult(
            SampleOutcome Outcome,
            AnnotationDocument? Document,
            string? Error = null);
    }
}
