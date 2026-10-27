using MediatR;

using Microsoft.Extensions.Logging;

using DeepVisionSharp.Application.Galleries;
using DeepVisionSharp.Domain.Entities;
using DeepVisionSharp.Domain.ValueObjects;
using DeepVisionSharp.Shell.Services;
using DeepVisionSharp.Shell.Services.Dialogs;
using DeepVisionSharp.Shell.ViewModels;
using DeepVisionSharp.Shell.ViewModels.Labeling;

namespace DeepVisionSharp.Shell.Services.Labeling;

/// <summary>
/// Sample delete, multi-select move, and NG(3) migrate actions for the labeling sample list.
/// Owned by <see cref="LabelingSampleListViewModel"/>.
/// </summary>
public sealed class LabelingSampleMigrateActions
{
    private readonly ILabelingSampleMigrateHost _host;
    private readonly IMediator _mediator;
    private readonly IAppDialogService _appDialogService;
    private readonly ShellSnackbarNotifier _snackbar;
    private readonly ILogger _logger;

    public LabelingSampleMigrateActions(
        ILabelingSampleMigrateHost host,
        IMediator mediator,
        IAppDialogService appDialogService,
        ShellSnackbarNotifier snackbar,
        ILogger logger)
    {
        _host = host;
        _mediator = mediator;
        _appDialogService = appDialogService;
        _snackbar = snackbar;
        _logger = logger;
    }

    public async Task ConfirmAndDeleteSampleAsync(Guid sampleId)
    {
        var gallery = _host.Context.Gallery;
        if (gallery is null)
        {
            return;
        }

        var sampleResult = await _mediator.Send(new GetGallerySampleQuery(gallery.Id, sampleId));
        if (sampleResult.IsFailure || sampleResult.Value is null)
        {
            _snackbar.ShowWarning(sampleResult.Error ?? "未找到要删除的样本。", "删除样本");
            return;
        }

        var sample = sampleResult.Value;
        var request = new ConfirmDialogRequest(
            $"即将删除样本「{sample.FileName}」及其标注数据。此操作不可恢复。");
        var result = await _appDialogService.ShowAsync(
            AppDialogKind.Confirm,
            request,
            new DialogOptions(
                Title: "确认删除样本",
                PrimaryButtonText: "删除",
                DefaultButton: AppDialogDefaultButton.Close));

        if (result != AppDialogResult.Primary)
        {
            return;
        }

        await DeleteSampleCoreAsync(gallery.Id, sampleId, sample.FileName);
    }

    public async Task ShowNgMoveTargetDialogAsync()
    {
        var gallery = _host.Context.Gallery;
        if (gallery is null)
        {
            _snackbar.ShowWarning("请先选择图库。", "NG 迁移");
            return;
        }

        if (!_host.Context.IsNotBusy)
        {
            return;
        }

        var selected = await PickNgMoveTargetGalleryAsync(gallery);
        if (selected is null)
        {
            return;
        }

        _host.NgMoveTargetGalleryId = selected.Value.Id;
        _host.NgMoveTargetGalleryName = selected.Value.Name;
        _snackbar.ShowSuccess($"NG(3) 目标图库已设为「{selected.Value.Name}」。", "NG 迁移");
    }

    public async Task SetImageClassNgAndMoveAsync()
    {
        var context = _host.Context;
        var gallery = context.Gallery;
        if (gallery is null
            || context.ActiveSampleId is null
            || string.IsNullOrWhiteSpace(context.ActiveFileName)
            || string.Equals(context.ActiveFileName, "未选择样本", StringComparison.Ordinal))
        {
            return;
        }

        if (!context.IsNotBusy)
        {
            return;
        }

        if (_host.NgMoveTargetGalleryId is null
            || string.IsNullOrWhiteSpace(_host.NgMoveTargetGalleryName)
            || _host.NgMoveTargetGalleryId == gallery.Id)
        {
            var selected = await PickNgMoveTargetGalleryAsync(gallery);
            if (selected is null)
            {
                return;
            }

            _host.NgMoveTargetGalleryId = selected.Value.Id;
            _host.NgMoveTargetGalleryName = selected.Value.Name;
        }

        var targetId = _host.NgMoveTargetGalleryId!.Value;
        var targetName = _host.NgMoveTargetGalleryName!;
        var fileName = context.ActiveFileName;

        var confirm = await _appDialogService.ShowAsync(
            AppDialogKind.Confirm,
            new ConfirmDialogRequest(
                $"将当前样本「{fileName}」标注为 NG，并迁移到图库「{targetName}」。\n\n是否继续？"),
            new DialogOptions(
                Title: "确认 NG 并迁移",
                PrimaryButtonText: "标注 NG 并迁移",
                CloseButtonText: "取消"));
        if (confirm != AppDialogResult.Primary)
        {
            return;
        }

        Guid? nextSampleId = null;
        var adjacentResult = await _mediator.Send(
            new GetAdjacentGallerySampleQuery(gallery.Id, context.ActiveSampleId.Value, 1));
        if (adjacentResult.IsSuccess && adjacentResult.Value is not null)
        {
            nextSampleId = adjacentResult.Value.Id;
        }

        try
        {
            await _host.ApplyImageClassNgAndSaveAsync();

            var moveResult = await _mediator.Send(new MoveSamplesCommand(
                gallery.Id,
                targetId,
                [fileName]));
            if (moveResult.IsFailure || moveResult.Value is null)
            {
                _snackbar.ShowError(moveResult.Error ?? "迁移样本失败。", "NG 迁移");
                return;
            }

            if (moveResult.Value.Failed > 0)
            {
                var error = moveResult.Value.Items.FirstOrDefault(item => !item.Succeeded)?.Error
                    ?? "迁移样本失败。";
                _snackbar.ShowError(error, "NG 迁移");
                return;
            }

            _host.SelectedSampleFileNames.Remove(fileName);
            _host.NotifyMoveSelectionStateChanged();
            await _host.RefreshFromGalleryAsync();

            if (context.SampleCount <= 0)
            {
                _host.ResetEditorState();
            }
            else if (nextSampleId is not null)
            {
                var stillExists = await _mediator.Send(new GetGallerySampleQuery(gallery.Id, nextSampleId.Value));
                if (stillExists.IsSuccess && stillExists.Value is not null)
                {
                    await _host.SelectSampleAsync(nextSampleId.Value);
                }
                else
                {
                    await SelectFirstRemainingSampleAsync(gallery.Id);
                }
            }
            else
            {
                await SelectFirstRemainingSampleAsync(gallery.Id);
            }

            var renameText = moveResult.Value.Renamed > 0 ? "（目标侧已自动重命名）" : string.Empty;
            _snackbar.ShowSuccess(
                $"已标注 NG 并迁移「{fileName}」到「{targetName}」{renameText}。",
                "NG 迁移");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "NG-and-move failed for {FileName}", fileName);
            _snackbar.ShowError($"NG 迁移失败：{ex.Message}", "NG 迁移");
        }
    }

    public async Task ShowMoveSamplesDialogAsync()
    {
        var context = _host.Context;
        var gallery = context.Gallery;
        if (gallery is null)
        {
            return;
        }

        var fileNamesToMove = ResolveMoveSampleFileNames();
        if (fileNamesToMove.Length == 0)
        {
            return;
        }

        var usedExplicitSelection = _host.SelectedSampleCount > 0;

        var galleriesResult = await _mediator.Send(new GetGalleriesByProjectQuery(gallery.ProjectId));
        if (galleriesResult.IsFailure || galleriesResult.Value is null)
        {
            _snackbar.ShowError(galleriesResult.Error ?? "加载目标图库失败。", "移动样本");
            return;
        }

        var dialog = _host.ReorganizeDialog;
        dialog.AvailableTargets.Clear();
        foreach (var target in galleriesResult.Value
                     .Where(item => item.Id != gallery.Id)
                     .OrderByDescending(item => item.SampleCount))
        {
            dialog.AvailableTargets.Add(
                new GalleryTargetOption(target.Id, target.Name, target.SampleCount));
        }

        var summary = usedExplicitSelection
            ? $"将把勾选的 {fileNamesToMove.Length} 张样本移动到其他图库。"
            : $"将把当前样本「{fileNamesToMove[0]}」移动到其他图库。";
        dialog.Reset($"{gallery.Name}-迁移", summary);

        while (true)
        {
            var result = await _appDialogService.ShowAsync(
                AppDialogKind.GalleryMoveSamples,
                dialog,
                new DialogOptions(Title: "移动到其他图库", PrimaryButtonText: "移动"));
            if (result != AppDialogResult.Primary)
            {
                return;
            }

            var canSubmit = dialog.UseNewGallery
                ? !string.IsNullOrWhiteSpace(dialog.NewGalleryName)
                : dialog.SelectedTargetGalleryId is not null;
            if (!canSubmit)
            {
                dialog.SetDialogError(
                    dialog.UseNewGallery ? "请填写新图库名称。" : "请选择目标图库。");
                continue;
            }

            Guid targetGalleryId;
            if (dialog.UseNewGallery)
            {
                var created = await _mediator.Send(new CreateGalleryCommand(
                    gallery.ProjectId,
                    dialog.NewGalleryName.Trim(),
                    string.Empty));
                if (created.IsFailure || created.Value is null)
                {
                    dialog.SetDialogError(created.Error ?? "创建目标图库失败。");
                    continue;
                }

                targetGalleryId = created.Value.Id;
            }
            else
            {
                targetGalleryId = dialog.SelectedTargetGalleryId!.Value;
            }

            var moveResult = await _mediator.Send(new MoveSamplesCommand(
                gallery.Id,
                targetGalleryId,
                fileNamesToMove));

            if (moveResult.IsFailure || moveResult.Value is null)
            {
                dialog.SetDialogError(moveResult.Error ?? "移动样本失败。");
                continue;
            }

            if (moveResult.Value.Failed > 0)
            {
                dialog.SetDialogError(
                    moveResult.Value.Items.FirstOrDefault(item => !item.Succeeded)?.Error ?? "移动样本失败。");
                continue;
            }

            var movedActiveSample = context.ActiveSampleId is not null
                && fileNamesToMove.Any(fileName =>
                    string.Equals(fileName, context.ActiveFileName, StringComparison.OrdinalIgnoreCase));

            if (usedExplicitSelection)
            {
                _host.SelectedSampleFileNames.Clear();
            }

            _host.NotifyMoveSelectionStateChanged();
            await _host.RefreshFromGalleryAsync();

            if (movedActiveSample || context.SampleCount == 0)
            {
                if (context.SampleCount > 0)
                {
                    var firstPageResult = await _mediator.Send(new ListGallerySamplesQuery(gallery.Id, 0, 1));
                    var firstSampleId = firstPageResult.IsSuccess
                        ? firstPageResult.Value?.Items.FirstOrDefault()?.Id
                        : null;
                    if (firstSampleId is not null)
                    {
                        await _host.SelectSampleAsync(firstSampleId.Value);
                    }
                }
                else
                {
                    _host.ResetEditorState();
                }
            }

            var batch = moveResult.Value;
            var renameText = batch.Renamed > 0 ? $"，其中 {batch.Renamed} 个样本已自动重命名" : string.Empty;
            _snackbar.ShowSuccess($"已移动 {batch.Moved} 张样本{renameText}。", "移动样本");
            return;
        }
    }

    private async Task DeleteSampleCoreAsync(Guid galleryId, Guid sampleId, string fileName)
    {
        var context = _host.Context;
        var deletedWasActive = context.ActiveSampleId == sampleId;
        Guid? successorSampleId = deletedWasActive
            ? await ResolveSuccessorSampleIdAsync(galleryId, sampleId)
            : null;

        await _mediator.Send(new DeleteSampleCommand(galleryId, fileName));
        _host.SelectedSampleFileNames.Remove(fileName);
        if (string.Equals(_host.AnchorFileName, fileName, StringComparison.OrdinalIgnoreCase))
        {
            _host.AnchorFileName = null;
        }

        _host.NotifyMoveSelectionStateChanged();
        await _host.RefreshFromGalleryAsync();
        _host.ApplyAnchorFlagsToRows();

        if (!deletedWasActive && context.ActiveSampleId is not null)
        {
            _snackbar.ShowSuccess($"已删除样本「{fileName}」。", "标注");
            return;
        }

        await SelectSampleAfterActiveRemovedAsync(galleryId, successorSampleId);
        _snackbar.ShowSuccess($"已删除样本「{fileName}」。", "标注");
    }

    /// <summary>
    /// Prefer the next sample in gallery order; fall back to previous when deleting the last one.
    /// Must be resolved before the active sample is removed.
    /// </summary>
    private async Task<Guid?> ResolveSuccessorSampleIdAsync(Guid galleryId, Guid currentSampleId)
    {
        var nextResult = await _mediator.Send(
            new GetAdjacentGallerySampleQuery(galleryId, currentSampleId, 1));
        if (nextResult.IsSuccess && nextResult.Value is not null)
        {
            return nextResult.Value.Id;
        }

        var previousResult = await _mediator.Send(
            new GetAdjacentGallerySampleQuery(galleryId, currentSampleId, -1));
        if (previousResult.IsSuccess && previousResult.Value is not null)
        {
            return previousResult.Value.Id;
        }

        return null;
    }

    private async Task SelectSampleAfterActiveRemovedAsync(Guid galleryId, Guid? preferredSampleId)
    {
        if (_host.Context.SampleCount <= 0)
        {
            _host.ResetEditorState();
            return;
        }

        if (preferredSampleId is not null)
        {
            var stillExists = await _mediator.Send(new GetGallerySampleQuery(galleryId, preferredSampleId.Value));
            if (stillExists.IsSuccess && stillExists.Value is not null)
            {
                await _host.SelectSampleAsync(preferredSampleId.Value);
                return;
            }
        }

        await SelectFirstRemainingSampleAsync(galleryId);
    }

    private async Task<(Guid Id, string Name)?> PickNgMoveTargetGalleryAsync(ProjectGallery gallery)
    {
        var galleriesResult = await _mediator.Send(new GetGalleriesByProjectQuery(gallery.ProjectId));
        if (galleriesResult.IsFailure || galleriesResult.Value is null)
        {
            _snackbar.ShowError(galleriesResult.Error ?? "加载目标图库失败。", "NG 迁移");
            return null;
        }

        var dialog = _host.ReorganizeDialog;
        dialog.AvailableTargets.Clear();
        foreach (var target in galleriesResult.Value
                     .Where(item => item.Id != gallery.Id)
                     .OrderByDescending(item => item.SampleCount)
                     .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            dialog.AvailableTargets.Add(
                new GalleryTargetOption(target.Id, target.Name, target.SampleCount));
        }

        if (dialog.AvailableTargets.Count == 0)
        {
            dialog.Reset(
                $"{gallery.Name}-NG",
                "当前项目没有其他图库，可新建一个作为 NG 迁移目标。");
            dialog.TargetMode = GalleryReorganizeDialogState.TargetModeNew;
        }
        else
        {
            dialog.Reset(
                $"{gallery.Name}-NG",
                "选择 NG(3) 的迁移目标图库。之后点击 NG(3) / 按快捷键 3 将标注为 NG 并迁移。");
            dialog.TargetMode = GalleryReorganizeDialogState.TargetModeExisting;
            if (_host.NgMoveTargetGalleryId is Guid existingId
                && dialog.AvailableTargets.Any(item => item.Id == existingId))
            {
                dialog.SelectedTargetGalleryId = existingId;
            }
        }

        while (true)
        {
            var result = await _appDialogService.ShowAsync(
                AppDialogKind.GalleryMoveSamples,
                dialog,
                new DialogOptions(Title: "选择 NG(3) 目标图库", PrimaryButtonText: "确定"));
            if (result != AppDialogResult.Primary)
            {
                return null;
            }

            if (dialog.UseNewGallery)
            {
                if (string.IsNullOrWhiteSpace(dialog.NewGalleryName))
                {
                    dialog.SetDialogError("请填写新图库名称。");
                    continue;
                }

                var created = await _mediator.Send(new CreateGalleryCommand(
                    gallery.ProjectId,
                    dialog.NewGalleryName.Trim(),
                    string.Empty));
                if (created.IsFailure || created.Value is null)
                {
                    dialog.SetDialogError(created.Error ?? "创建目标图库失败。");
                    continue;
                }

                return (created.Value.Id, created.Value.Name);
            }

            if (dialog.SelectedTargetGalleryId is not Guid targetId)
            {
                dialog.SetDialogError("请选择目标图库。");
                continue;
            }

            var option = dialog.AvailableTargets.FirstOrDefault(item => item.Id == targetId);
            if (option is null)
            {
                dialog.SetDialogError("请选择有效的目标图库。");
                continue;
            }

            return (option.Id, option.Name);
        }
    }

    private async Task SelectFirstRemainingSampleAsync(Guid galleryId)
    {
        var firstPageResult = await _mediator.Send(new ListGallerySamplesQuery(galleryId, 0, 1));
        var firstSampleId = firstPageResult.IsSuccess
            ? firstPageResult.Value?.Items.FirstOrDefault()?.Id
            : null;
        if (firstSampleId is not null)
        {
            await _host.SelectSampleAsync(firstSampleId.Value);
        }
        else
        {
            _host.ResetEditorState();
        }
    }

    /// <summary>
    /// 有勾选 → 勾选集合；无勾选 → 当前打开样本（若有）。
    /// </summary>
    private string[] ResolveMoveSampleFileNames()
    {
        if (_host.SelectedSampleCount > 0)
        {
            return _host.SelectedSampleFileNames.ToArray();
        }

        var context = _host.Context;
        if (context.ActiveSampleId is not null
            && !string.IsNullOrWhiteSpace(context.ActiveFileName)
            && !string.Equals(context.ActiveFileName, "未选择样本", StringComparison.Ordinal))
        {
            return [context.ActiveFileName];
        }

        return [];
    }
}

/// <summary>Host surface for <see cref="LabelingSampleMigrateActions"/>.</summary>
public interface ILabelingSampleMigrateHost
{
    LabelingWorkspaceContext Context { get; }

    GalleryReorganizeDialogState ReorganizeDialog { get; }

    HashSet<string> SelectedSampleFileNames { get; }

    int SelectedSampleCount { get; }

    Guid? NgMoveTargetGalleryId { get; set; }

    string? NgMoveTargetGalleryName { get; set; }

    string? AnchorFileName { get; set; }

    Task RefreshFromGalleryAsync(CancellationToken cancellationToken = default);

    Task SelectSampleAsync(Guid sampleId);

    Task ApplyImageClassNgAndSaveAsync();

    void ResetEditorState();

    void ApplyAnchorFlagsToRows();

    void NotifyMoveSelectionStateChanged();
}
