using DeepVisionSharp.Application.Common;
using DeepVisionSharp.Domain.Common;
using DeepVisionSharp.Domain.Repositories;

namespace DeepVisionSharp.Application.Galleries;

public enum GallerySplitMode
{
    Manual,
    ByRatio,
    ByCount,
    Annotated,
    Unannotated,
    FileNamePrefix,
    WithError,
    ByLabel,
    ByLabelRatio
}

public sealed class GallerySplitSelectionResult
{
    public GallerySplitSelectionResult(
        IReadOnlyList<string> fileNamesToMove,
        int totalInGallery,
        int matchedCount,
        string previewText)
    {
        FileNamesToMove = fileNamesToMove;
        TotalInGallery = totalInGallery;
        MatchedCount = matchedCount;
        PreviewText = previewText;
    }

    public IReadOnlyList<string> FileNamesToMove { get; }

    public int TotalInGallery { get; }

    public int MatchedCount { get; }

    public string PreviewText { get; }
}

public sealed record ResolveGallerySplitSelectionQuery(
    Guid GalleryId,
    GallerySplitMode Mode,
    double MoveRatio = 0.8,
    int? RandomSeed = null,
    bool PreserveFileOrder = false,
    IReadOnlyList<string>? ManualFileNames = null,
    string? FileNamePrefix = null,
    string? TargetLabel = null,
    int MoveCount = 0) : IQuery<GallerySplitSelectionResult>;

public sealed class ResolveGallerySplitSelectionQueryHandler
    : IQueryHandler<ResolveGallerySplitSelectionQuery, GallerySplitSelectionResult>
{
    private readonly IGalleryRepository _galleries;
    private readonly IGalleryAnnotationLabelQueryService _annotationLabels;

    public ResolveGallerySplitSelectionQueryHandler(
        IGalleryRepository galleries,
        IGalleryAnnotationLabelQueryService annotationLabels)
    {
        _galleries = galleries;
        _annotationLabels = annotationLabels;
    }

    public async Task<Result<GallerySplitSelectionResult>> Handle(
        ResolveGallerySplitSelectionQuery request,
        CancellationToken cancellationToken)
    {
        var summary = await _galleries.GetSummaryByIdAsync(request.GalleryId, cancellationToken);
        if (summary is null)
        {
            return Result<GallerySplitSelectionResult>.Failure("图库不存在。");
        }

        if (summary.SampleCount < 2)
        {
            return Result<GallerySplitSelectionResult>.Failure("图库样本不足，无法拆分。");
        }

        return request.Mode switch
        {
            GallerySplitMode.Manual => ResolveManual(summary.SampleCount, request.ManualFileNames),
            GallerySplitMode.ByRatio => await ResolveByRatioAsync(request, summary.SampleCount, cancellationToken),
            GallerySplitMode.ByCount => await ResolveByCountAsync(request, summary.SampleCount, cancellationToken),
            GallerySplitMode.Annotated => await ResolveByAnnotationAsync(
                request.GalleryId,
                summary.SampleCount,
                isAnnotated: true,
                cancellationToken),
            GallerySplitMode.Unannotated => await ResolveByAnnotationAsync(
                request.GalleryId,
                summary.SampleCount,
                isAnnotated: false,
                cancellationToken),
            GallerySplitMode.FileNamePrefix => await ResolveByFileNamePrefixAsync(
                request,
                summary.SampleCount,
                cancellationToken),
            GallerySplitMode.WithError => await ResolveByErrorStatusAsync(
                request.GalleryId,
                summary.SampleCount,
                cancellationToken),
            GallerySplitMode.ByLabel => await ResolveByLabelAsync(
                request,
                summary.SampleCount,
                cancellationToken),
            GallerySplitMode.ByLabelRatio => await ResolveByLabelRatioAsync(
                request,
                summary.SampleCount,
                cancellationToken),
            _ => Result<GallerySplitSelectionResult>.Failure("不支持的拆分模式。")
        };
    }

    private static Result<GallerySplitSelectionResult> ResolveManual(
        int totalInGallery,
        IReadOnlyList<string>? manualFileNames)
    {
        if (manualFileNames is null || manualFileNames.Count == 0)
        {
            return Result<GallerySplitSelectionResult>.Failure("请至少选择一个样本。");
        }

        var distinct = manualFileNames
            .Select(Path.GetFileName)
            .Where(fileName => !string.IsNullOrWhiteSpace(fileName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
            .ToArray();

        if (distinct.Length == 0)
        {
            return Result<GallerySplitSelectionResult>.Failure("请至少选择一个样本。");
        }

        if (distinct.Length >= totalInGallery)
        {
            return Result<GallerySplitSelectionResult>.Failure("拆分后源图库至少应保留 1 个样本。");
        }

        var preview = $"将移动 {distinct.Length} 张样本，保留 {totalInGallery - distinct.Length} 张。";
        return Result<GallerySplitSelectionResult>.Success(
            new GallerySplitSelectionResult(distinct, totalInGallery, distinct.Length, preview));
    }

    private async Task<Result<GallerySplitSelectionResult>> ResolveByRatioAsync(
        ResolveGallerySplitSelectionQuery request,
        int totalInGallery,
        CancellationToken cancellationToken)
    {
        var allNames = await _galleries.ListSampleFileNamesAsync(request.GalleryId, null, cancellationToken);
        var toMove = GallerySampleSelection.SelectByMoveRatio(
            allNames,
            request.MoveRatio,
            request.RandomSeed,
            request.PreserveFileOrder);

        if (toMove.Count == 0)
        {
            return Result<GallerySplitSelectionResult>.Failure("无法按比例生成有效的拆分结果。");
        }

        var moveRatioPercent = (int)Math.Round(Math.Clamp(request.MoveRatio, 0.05, 0.95) * 100);
        var shuffleText = request.PreserveFileOrder ? "按文件名顺序" : "随机抽样";
        var preview =
            $"将移动 {toMove.Count} 张样本（约 {moveRatioPercent}%，{shuffleText}），保留 {totalInGallery - toMove.Count} 张。";
        return Result<GallerySplitSelectionResult>.Success(
            new GallerySplitSelectionResult(toMove, totalInGallery, allNames.Count, preview));
    }

    private async Task<Result<GallerySplitSelectionResult>> ResolveByCountAsync(
        ResolveGallerySplitSelectionQuery request,
        int totalInGallery,
        CancellationToken cancellationToken)
    {
        var maxMove = totalInGallery - 1;
        if (request.MoveCount < 1 || request.MoveCount > maxMove)
        {
            return Result<GallerySplitSelectionResult>.Failure(
                $"移动数量须在 1～{maxMove} 之间，且源图库至少保留 1 张。");
        }

        var allNames = await _galleries.ListSampleFileNamesAsync(request.GalleryId, null, cancellationToken);
        var toMove = GallerySampleSelection.SelectByMoveCount(
            allNames,
            request.MoveCount,
            request.RandomSeed,
            request.PreserveFileOrder);

        if (toMove.Count == 0)
        {
            return Result<GallerySplitSelectionResult>.Failure("无法按数量生成有效的拆分结果。");
        }

        if (toMove.Count >= totalInGallery)
        {
            return Result<GallerySplitSelectionResult>.Failure("拆分后源图库至少应保留 1 个样本。");
        }

        var shuffleText = request.PreserveFileOrder ? "按文件名顺序" : "随机抽样";
        var preview =
            $"将移动 {toMove.Count} 张样本（指定数量，{shuffleText}），保留 {totalInGallery - toMove.Count} 张。";
        return Result<GallerySplitSelectionResult>.Success(
            new GallerySplitSelectionResult(toMove, totalInGallery, allNames.Count, preview));
    }

    private async Task<Result<GallerySplitSelectionResult>> ResolveByAnnotationAsync(
        Guid galleryId,
        int totalInGallery,
        bool isAnnotated,
        CancellationToken cancellationToken)
    {
        var matched = await _galleries.ListSampleFileNamesAsync(
            galleryId,
            new GallerySampleFilter { IsAnnotated = isAnnotated },
            cancellationToken);

        if (matched.Count == 0)
        {
            return Result<GallerySplitSelectionResult>.Failure(
                isAnnotated ? "没有已标注样本可拆分。" : "没有未标注样本可拆分。");
        }

        if (matched.Count >= totalInGallery)
        {
            return Result<GallerySplitSelectionResult>.Failure("拆分后源图库至少应保留 1 个样本。");
        }

        var label = isAnnotated ? "已标注" : "未标注";
        var preview = $"将移动 {matched.Count} 张{label}样本，保留 {totalInGallery - matched.Count} 张。";
        return Result<GallerySplitSelectionResult>.Success(
            new GallerySplitSelectionResult(matched, totalInGallery, matched.Count, preview));
    }

    private async Task<Result<GallerySplitSelectionResult>> ResolveByFileNamePrefixAsync(
        ResolveGallerySplitSelectionQuery request,
        int totalInGallery,
        CancellationToken cancellationToken)
    {
        var prefix = request.FileNamePrefix?.Trim();
        if (string.IsNullOrWhiteSpace(prefix))
        {
            return Result<GallerySplitSelectionResult>.Failure("请填写文件名前缀。");
        }

        var candidates = await _galleries.ListSampleFileNamesAsync(
            request.GalleryId,
            new GallerySampleFilter { FileNamePrefix = prefix },
            cancellationToken);

        // The repository filter becomes LIKE 'prefix%', where % and _ are wildcards, so it returns a
        // superset: "IMG_0" also matches "IMG-01". Narrow to literal prefix matches here, which is
        // what the dialog promises the user.
        var matched = candidates
            .Where(name => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matched.Count == 0)
        {
            return Result<GallerySplitSelectionResult>.Failure("没有匹配该前缀的样本可拆分。");
        }

        if (matched.Count >= totalInGallery)
        {
            return Result<GallerySplitSelectionResult>.Failure("拆分后源图库至少应保留 1 个样本。");
        }

        var preview = $"将移动 {matched.Count} 张前缀为「{prefix}」的样本，保留 {totalInGallery - matched.Count} 张。";
        return Result<GallerySplitSelectionResult>.Success(
            new GallerySplitSelectionResult(matched, totalInGallery, matched.Count, preview));
    }

    private async Task<Result<GallerySplitSelectionResult>> ResolveByErrorStatusAsync(
        Guid galleryId,
        int totalInGallery,
        CancellationToken cancellationToken)
    {
        var matched = await _galleries.ListSampleFileNamesAsync(
            galleryId,
            new GallerySampleFilter { HasError = true },
            cancellationToken);

        if (matched.Count == 0)
        {
            return Result<GallerySplitSelectionResult>.Failure("没有错误样本可拆分。");
        }

        if (matched.Count >= totalInGallery)
        {
            return Result<GallerySplitSelectionResult>.Failure("拆分后源图库至少应保留 1 个样本。");
        }

        var preview = $"将移动 {matched.Count} 张错误样本，保留 {totalInGallery - matched.Count} 张。";
        return Result<GallerySplitSelectionResult>.Success(
            new GallerySplitSelectionResult(matched, totalInGallery, matched.Count, preview));
    }

    private async Task<Result<GallerySplitSelectionResult>> ResolveByLabelAsync(
        ResolveGallerySplitSelectionQuery request,
        int totalInGallery,
        CancellationToken cancellationToken)
    {
        var targetLabel = request.TargetLabel?.Trim();
        if (string.IsNullOrWhiteSpace(targetLabel))
        {
            return Result<GallerySplitSelectionResult>.Failure("请选择缺陷类别。");
        }

        var matched = await _annotationLabels.ListSampleFileNamesWithLabelAsync(
            request.GalleryId,
            targetLabel,
            GalleryAnnotationLabelMatch.Any,
            cancellationToken);

        if (matched.Count == 0)
        {
            return Result<GallerySplitSelectionResult>.Failure($"没有包含「{targetLabel}」标注的样本可拆分。");
        }

        if (matched.Count >= totalInGallery)
        {
            return Result<GallerySplitSelectionResult>.Failure("拆分后源图库至少应保留 1 个样本。");
        }

        var preview = $"将移动 {matched.Count} 张包含「{targetLabel}」标注的样本，保留 {totalInGallery - matched.Count} 张。";
        return Result<GallerySplitSelectionResult>.Success(
            new GallerySplitSelectionResult(matched, totalInGallery, matched.Count, preview));
    }

    private async Task<Result<GallerySplitSelectionResult>> ResolveByLabelRatioAsync(
        ResolveGallerySplitSelectionQuery request,
        int totalInGallery,
        CancellationToken cancellationToken)
    {
        var targetLabel = request.TargetLabel?.Trim();
        if (string.IsNullOrWhiteSpace(targetLabel))
        {
            return Result<GallerySplitSelectionResult>.Failure("请选择缺陷类别。");
        }

        var matched = await _annotationLabels.ListSampleFileNamesWithLabelAsync(
            request.GalleryId,
            targetLabel,
            GalleryAnnotationLabelMatch.Any,
            cancellationToken);

        if (matched.Count < 2)
        {
            return Result<GallerySplitSelectionResult>.Failure($"「{targetLabel}」类别样本不足，无法按比例拆分。");
        }

        var toMove = GallerySampleSelection.SelectByMoveRatio(
            matched,
            request.MoveRatio,
            request.RandomSeed,
            request.PreserveFileOrder);

        if (toMove.Count == 0)
        {
            return Result<GallerySplitSelectionResult>.Failure("无法按比例生成有效的拆分结果。");
        }

        if (toMove.Count >= totalInGallery)
        {
            return Result<GallerySplitSelectionResult>.Failure("拆分后源图库至少应保留 1 个样本。");
        }

        var moveRatioPercent = (int)Math.Round(Math.Clamp(request.MoveRatio, 0.05, 0.95) * 100);
        var shuffleText = request.PreserveFileOrder ? "按文件名顺序" : "随机抽样";
        var preview =
            $"将移动 {toMove.Count} 张「{targetLabel}」样本（约 {moveRatioPercent}%，{shuffleText}），保留 {totalInGallery - toMove.Count} 张。";
        return Result<GallerySplitSelectionResult>.Success(
            new GallerySplitSelectionResult(toMove, totalInGallery, matched.Count, preview));
    }
}

public sealed record ListGalleryAnnotationLabelsQuery(Guid GalleryId) : IQuery<IReadOnlyList<string>>;

public sealed class ListGalleryAnnotationLabelsQueryHandler
    : IQueryHandler<ListGalleryAnnotationLabelsQuery, IReadOnlyList<string>>
{
    private readonly IGalleryAnnotationLabelQueryService _annotationLabels;

    public ListGalleryAnnotationLabelsQueryHandler(IGalleryAnnotationLabelQueryService annotationLabels)
    {
        _annotationLabels = annotationLabels;
    }

    public async Task<Result<IReadOnlyList<string>>> Handle(
        ListGalleryAnnotationLabelsQuery request,
        CancellationToken cancellationToken)
    {
        var labels = await _annotationLabels.ListDistinctLabelsAsync(request.GalleryId, cancellationToken);
        return Result<IReadOnlyList<string>>.Success(labels);
    }
}
