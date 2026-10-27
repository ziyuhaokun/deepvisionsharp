using DeepVisionSharp.Application.Common;
using DeepVisionSharp.Domain.Common;
using DeepVisionSharp.Domain.Entities;
using DeepVisionSharp.Domain.Repositories;

namespace DeepVisionSharp.Application.Galleries;

public sealed record MoveSamplesCommand(
    Guid SourceGalleryId,
    Guid TargetGalleryId,
    IReadOnlyList<string> FileNames) : ICommand<SampleBatchMoveResult>;

public sealed class MoveSamplesCommandHandler : ICommandHandler<MoveSamplesCommand, SampleBatchMoveResult>
{
    private readonly ISampleMigrationService _migration;

    public MoveSamplesCommandHandler(ISampleMigrationService migration)
    {
        _migration = migration;
    }

    public Task<Result<SampleBatchMoveResult>> Handle(MoveSamplesCommand request, CancellationToken cancellationToken)
    {
        return _migration.MoveManyAsync(
            request.SourceGalleryId,
            request.TargetGalleryId,
            request.FileNames,
            new SampleMoveOptions(AutoRenameOnConflict: true),
            progress: null,
            cancellationToken);
    }
}

public sealed record SplitGalleryCommand(
    Guid SourceGalleryId,
    IReadOnlyList<string> FileNames,
    Guid? TargetGalleryId,
    string? NewGalleryName,
    string NewGalleryDescription = "") : ICommand<SampleBatchMoveResult>;

public sealed class SplitGalleryCommandHandler : ICommandHandler<SplitGalleryCommand, SampleBatchMoveResult>
{
    private readonly IGalleryRepository _galleries;
    private readonly ISampleMigrationService _migration;

    public SplitGalleryCommandHandler(IGalleryRepository galleries, ISampleMigrationService migration)
    {
        _galleries = galleries;
        _migration = migration;
    }

    public async Task<Result<SampleBatchMoveResult>> Handle(
        SplitGalleryCommand request,
        CancellationToken cancellationToken)
    {
        if (request.FileNames.Count == 0)
        {
            return Result<SampleBatchMoveResult>.Failure("请至少选择一个样本。");
        }

        var sourceGallery = await _galleries.GetSummaryByIdAsync(request.SourceGalleryId, cancellationToken);
        if (sourceGallery is null)
        {
            return Result<SampleBatchMoveResult>.Failure("源图库不存在。");
        }

        if (request.FileNames.Count >= sourceGallery.SampleCount)
        {
            return Result<SampleBatchMoveResult>.Failure("拆分后源图库至少应保留 1 个样本。");
        }

        var targetGalleryId = request.TargetGalleryId;
        var createdTarget = false;
        if (targetGalleryId is null)
        {
            if (string.IsNullOrWhiteSpace(request.NewGalleryName))
            {
                return Result<SampleBatchMoveResult>.Failure("请填写新图库名称。");
            }

            // Same uniqueness rule as CreateGalleryCommand: creating a gallery here must not be a
            // back door around it, or the project ends up with two galleries of the same name.
            if (await _galleries.ExistsByNameAsync(
                    sourceGallery.ProjectId,
                    request.NewGalleryName.Trim(),
                    cancellationToken: cancellationToken).ConfigureAwait(false))
            {
                return Result<SampleBatchMoveResult>.Failure(
                    $"图库名称「{request.NewGalleryName.Trim()}」已存在。");
            }

            var created = await _galleries.CreateAsync(
                sourceGallery.ProjectId,
                request.NewGalleryName.Trim(),
                request.NewGalleryDescription.Trim(),
                cancellationToken);
            targetGalleryId = created.Id;
            createdTarget = true;
        }
        else if (targetGalleryId.Value == request.SourceGalleryId)
        {
            return Result<SampleBatchMoveResult>.Failure("源图库和目标图库不能相同。");
        }

        var moveResult = await _migration.MoveManyAsync(
            request.SourceGalleryId,
            targetGalleryId.Value,
            request.FileNames,
            new SampleMoveOptions(AutoRenameOnConflict: true),
            progress: null,
            cancellationToken);

        if ((moveResult.IsFailure || moveResult.Value is null || moveResult.Value.Failed > 0)
            && createdTarget)
        {
            await TryDeleteEmptyGalleryAsync(targetGalleryId.Value, cancellationToken);
        }

        return moveResult;
    }

    private async Task TryDeleteEmptyGalleryAsync(Guid galleryId, CancellationToken cancellationToken)
    {
        var summary = await _galleries.GetSummaryByIdAsync(galleryId, cancellationToken);
        if (summary is { SampleCount: 0 })
        {
            await _galleries.DeleteAsync(galleryId, cancellationToken);
        }
    }
}

public sealed record MergeGalleriesCommand(
    Guid ProjectId,
    IReadOnlyList<Guid> SourceGalleryIds,
    Guid? TargetGalleryId,
    string? NewGalleryName,
    string NewGalleryDescription = "",
    bool DeleteEmptySources = true) : ICommand<GalleryMergeResult>;

public sealed class GalleryMergeResult
{
    public GalleryMergeResult(
        Guid targetGalleryId,
        SampleBatchMoveResult moveResult,
        int deletedSourceCount)
    {
        TargetGalleryId = targetGalleryId;
        MoveResult = moveResult;
        DeletedSourceCount = deletedSourceCount;
    }

    public Guid TargetGalleryId { get; }

    public SampleBatchMoveResult MoveResult { get; }

    public int DeletedSourceCount { get; }
}

public sealed class MergeGalleriesCommandHandler : ICommandHandler<MergeGalleriesCommand, GalleryMergeResult>
{
    private readonly IGalleryRepository _galleries;
    private readonly ISampleMigrationService _migration;

    public MergeGalleriesCommandHandler(IGalleryRepository galleries, ISampleMigrationService migration)
    {
        _galleries = galleries;
        _migration = migration;
    }

    public async Task<Result<GalleryMergeResult>> Handle(
        MergeGalleriesCommand request,
        CancellationToken cancellationToken)
    {
        var sourceIds = request.SourceGalleryIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToArray();
        if (sourceIds.Length < 2)
        {
            return Result<GalleryMergeResult>.Failure("请至少选择 2 个图库进行合并。");
        }

        var summaries = new List<ProjectGallery>();
        foreach (var sourceId in sourceIds)
        {
            var summary = await _galleries.GetSummaryByIdAsync(sourceId, cancellationToken);
            if (summary is null)
            {
                return Result<GalleryMergeResult>.Failure("所选图库不存在。");
            }

            if (summary.ProjectId != request.ProjectId)
            {
                return Result<GalleryMergeResult>.Failure("仅支持合并同一项目内的图库。");
            }

            summaries.Add(summary);
        }

        if (request.TargetGalleryId is { } existingTargetId)
        {
            var targetSummary = await _galleries.GetSummaryByIdAsync(existingTargetId, cancellationToken);
            if (targetSummary is null)
            {
                return Result<GalleryMergeResult>.Failure("目标图库不存在。");
            }
        }

        var targetGalleryId = request.TargetGalleryId;
        var createdTarget = false;
        if (targetGalleryId is null)
        {
            if (string.IsNullOrWhiteSpace(request.NewGalleryName))
            {
                return Result<GalleryMergeResult>.Failure("请填写合并后的图库名称。");
            }

            if (await _galleries.ExistsByNameAsync(
                    request.ProjectId,
                    request.NewGalleryName.Trim(),
                    cancellationToken: cancellationToken).ConfigureAwait(false))
            {
                return Result<GalleryMergeResult>.Failure(
                    $"图库名称「{request.NewGalleryName.Trim()}」已存在。");
            }

            var created = await _galleries.CreateAsync(
                request.ProjectId,
                request.NewGalleryName.Trim(),
                request.NewGalleryDescription.Trim(),
                cancellationToken);
            targetGalleryId = created.Id;
            createdTarget = true;
        }

        var moveSources = sourceIds.Where(id => id != targetGalleryId.Value).ToArray();
        SampleBatchMoveResult? aggregateResult = null;
        var totalMoved = 0;
        var totalRenamed = 0;
        var totalFailed = 0;
        var allItems = new List<SampleMoveItemResult>();

        foreach (var sourceId in moveSources)
        {
            var sourceGallery = await _galleries.GetSummaryByIdAsync(sourceId, cancellationToken);
            if (sourceGallery is null)
            {
                await TryDeleteEmptyCreatedTargetAsync(createdTarget, targetGalleryId.Value, totalMoved, cancellationToken);
                return Result<GalleryMergeResult>.Failure("合并过程中源图库不存在。");
            }

            if (sourceGallery.SampleCount == 0)
            {
                continue;
            }

            var fileNames = await _galleries.ListSampleFileNamesAsync(sourceId, cancellationToken: cancellationToken);
            var moveResult = await _migration.MoveManyAsync(
                sourceId,
                targetGalleryId.Value,
                fileNames,
                new SampleMoveOptions(AutoRenameOnConflict: true),
                progress: null,
                cancellationToken);
            if (moveResult.IsFailure || moveResult.Value is null)
            {
                await TryDeleteEmptyCreatedTargetAsync(createdTarget, targetGalleryId.Value, totalMoved, cancellationToken);
                return Result<GalleryMergeResult>.Failure(moveResult.Error ?? "合并样本失败。");
            }

            aggregateResult = moveResult.Value;
            totalMoved += moveResult.Value.Moved;
            totalRenamed += moveResult.Value.Renamed;
            totalFailed += moveResult.Value.Failed;
            allItems.AddRange(moveResult.Value.Items);

            if (moveResult.Value.Failed > 0)
            {
                await TryDeleteEmptyCreatedTargetAsync(createdTarget, targetGalleryId.Value, totalMoved, cancellationToken);
                return Result<GalleryMergeResult>.Failure(
                    $"合并失败：{moveResult.Value.Items.FirstOrDefault(item => !item.Succeeded)?.Error ?? "未知错误"}");
            }
        }

        var deletedSourceCount = 0;
        if (request.DeleteEmptySources)
        {
            foreach (var sourceId in moveSources)
            {
                var remaining = await _galleries.GetSummaryByIdAsync(sourceId, cancellationToken);
                if (remaining is { SampleCount: 0 }
                    && await _galleries.DeleteAsync(sourceId, cancellationToken))
                {
                    deletedSourceCount++;
                }
            }
        }

        var combined = aggregateResult is null
            ? new SampleBatchMoveResult(true, 0, 0, 0, 0, [])
            : new SampleBatchMoveResult(
                aggregateResult.Succeeded && totalFailed == 0,
                totalMoved + totalFailed,
                totalMoved,
                totalRenamed,
                totalFailed,
                allItems);

        return Result<GalleryMergeResult>.Success(
            new GalleryMergeResult(targetGalleryId.Value, combined, deletedSourceCount));
    }

    /// <summary>
    /// Compensates a failed merge that created a brand-new empty target gallery.
    /// Partial moves that already landed samples are intentionally left in place
    /// (full reverse migration is not automatic).
    /// </summary>
    private async Task TryDeleteEmptyCreatedTargetAsync(
        bool createdTarget,
        Guid targetGalleryId,
        int totalMovedSoFar,
        CancellationToken cancellationToken)
    {
        if (!createdTarget || totalMovedSoFar > 0)
        {
            return;
        }

        var summary = await _galleries.GetSummaryByIdAsync(targetGalleryId, cancellationToken);
        if (summary is { SampleCount: 0 })
        {
            await _galleries.DeleteAsync(targetGalleryId, cancellationToken);
        }
    }
}
