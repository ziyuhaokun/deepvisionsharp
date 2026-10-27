using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediatR;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using DeepVisionSharp.Application.Galleries;
using DeepVisionSharp.Shell.Services;
using DeepVisionSharp.Shell.Services.Dialogs;
using DeepVisionSharp.Shell.ViewModels;

namespace DeepVisionSharp.Shell.ViewModels.Gallery;

public sealed partial class GalleryReorganizeViewModel : ObservableObject
{
    private readonly IMediator _mediator;
    private readonly GalleryListViewModel _list;
    private readonly ShellSnackbarNotifier _snackbar;
    private readonly IAppDialogService _appDialogService;
    private readonly ILogger<GalleryReorganizeViewModel> _logger;

    private Guid? _splitSourceGalleryId;
    private string[] _splitSelectedFileNames = [];
    private readonly List<GallerySamplePickerRow> _samplePickerRows = [];
    private CancellationTokenSource? _splitPreviewCancellation;
    private int _splitPreviewVersion;

    public GalleryReorganizeViewModel(
        IMediator mediator,
        GalleryListViewModel list,
        ShellSnackbarNotifier snackbar,
        IAppDialogService appDialogService,
        ILogger<GalleryReorganizeViewModel> logger)
    {
        _mediator = mediator;
        _list = list;
        _snackbar = snackbar;
        _appDialogService = appDialogService;
        _logger = logger;
        SamplePicker.PropertyChanged += OnSamplePickerPropertyChanged;
        SplitPlanner.PropertyChanged += OnSplitPlannerPropertyChanged;
        _list.SelectionStateChanged += NotifyMergeCommandState;
        _list.BusyStateAffectingCommandsChanged += NotifyBusyCommands;
    }

    public GalleryReorganizeDialogState ReorganizeDialog { get; } = new();

    public GallerySamplePickerState SamplePicker { get; } = new();

    public GallerySplitPlannerState SplitPlanner { get; } = new();

    public bool CanMergeGalleries => _list.CanMergeGalleries;

    public bool CanSubmitGalleryReorganize =>
        ReorganizeDialog.UseNewGallery
            ? !string.IsNullOrWhiteSpace(ReorganizeDialog.NewGalleryName)
            : ReorganizeDialog.SelectedTargetGalleryId is not null;

    [RelayCommand(CanExecute = nameof(CanMergeGalleries))]
    private async Task ShowMergeGalleriesDialogAsync()
    {
        if (_list.CurrentProjectId is null)
        {
            _snackbar.ShowWarning("请先选择项目。", "合并图库");
            return;
        }

        var selected = _list.Galleries.Where(gallery => gallery.IsSelected).ToArray();
        if (selected.Length < 2)
        {
            _snackbar.ShowWarning("请至少选择 2 个图库。", "合并图库");
            return;
        }

        ReorganizeDialog.AvailableTargets.Clear();
        foreach (var gallery in _list.Galleries
                     .Where(gallery => !gallery.IsSelected)
                     .OrderByDescending(gallery => gallery.SampleCount))
        {
            ReorganizeDialog.AvailableTargets.Add(
                new GalleryTargetOption(gallery.Id, gallery.Name, gallery.SampleCount));
        }

        var totalSamples = selected.Sum(gallery => gallery.SampleCount);
        ReorganizeDialog.Reset(
            $"合并图库 {_list.GalleryCount + 1}",
            $"将合并 {selected.Length} 个图库，共 {totalSamples} 张样本。",
            showDeleteEmptySourcesOption: true);

        while (true)
        {
            var result = await _appDialogService.ShowAsync(
                AppDialogKind.GalleryMerge,
                ReorganizeDialog,
                new DialogOptions(Title: "合并图库", PrimaryButtonText: "合并"));
            if (result != AppDialogResult.Primary)
            {
                return;
            }

            if (!CanSubmitGalleryReorganize)
            {
                ReorganizeDialog.SetDialogError(
                    ReorganizeDialog.UseNewGallery ? "请填写新图库名称。" : "请选择目标图库。");
                continue;
            }

            var sourceIds = selected.Select(gallery => gallery.Id).ToArray();
            var mergeResult = await _mediator.Send(new MergeGalleriesCommand(
                _list.CurrentProjectId.Value,
                sourceIds,
                ReorganizeDialog.UseExistingGallery ? ReorganizeDialog.SelectedTargetGalleryId : null,
                ReorganizeDialog.UseNewGallery ? ReorganizeDialog.NewGalleryName.Trim() : null,
                DeleteEmptySources: ReorganizeDialog.DeleteEmptySources));

            if (mergeResult.IsFailure || mergeResult.Value is null)
            {
                ReorganizeDialog.SetDialogError(mergeResult.Error ?? "合并图库失败。");
                continue;
            }

            await _list.RefreshCurrentProjectGalleriesAsync();
            await _list.RecalculateProjectProgressAsync();
            _list.ClearSelectionMode();

            var moved = mergeResult.Value.MoveResult.Moved;
            var renamed = mergeResult.Value.MoveResult.Renamed;
            var renameText = renamed > 0 ? $"，其中 {renamed} 个样本已自动重命名" : string.Empty;
            _snackbar.ShowSuccess(
                $"已合并 {moved} 张样本到目标图库{renameText}。",
                "合并图库");
            return;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRunGalleryOperation))]
    private async Task ShowSplitGalleryDialogAsync(Guid galleryId)
    {
        var gallery = _list.Galleries.FirstOrDefault(item => item.Id == galleryId);
        if (gallery is null)
        {
            _snackbar.ShowWarning("未找到图库。", "拆分图库");
            return;
        }

        if (gallery.SampleCount < 2)
        {
            _snackbar.ShowWarning("图库样本不足，无法拆分。", "拆分图库");
            return;
        }

        _splitSourceGalleryId = galleryId;
        _splitSelectedFileNames = [];
        SplitPlanner.Reset(gallery.Name, gallery.SampleCount);
        await LoadSplitPlannerLabelsAsync(galleryId);
        await RefreshSplitPlannerPreviewAsync();

        var useManualPicker = false;
        while (true)
        {
            var plannerResult = await _appDialogService.ShowAsync(
                AppDialogKind.GallerySplitPlanner,
                SplitPlanner,
                new DialogOptions(
                    Title: "拆分图库",
                    PrimaryButtonText: "下一步",
                    PrimaryButtonIsEnabledBindingPath: nameof(GallerySplitPlannerState.CanContinue)));
            if (plannerResult != AppDialogResult.Primary)
            {
                CancelSplitPreview();
                _splitSourceGalleryId = null;
                return;
            }

            if (!SplitPlanner.CanContinue)
            {
                SplitPlanner.SetDialogError(
                    SplitPlanner.HasRandomSeedError
                        ? "随机种子必须是整数。"
                        : "请先完成有效的拆分规则配置。");
                continue;
            }

            var splitMode = SplitPlanner.ToSplitMode();
            if (splitMode == GallerySplitMode.Manual)
            {
                useManualPicker = true;
                break;
            }

            var resolveResult = await _mediator.Send(BuildSplitSelectionQuery(galleryId));
            if (resolveResult.IsFailure || resolveResult.Value is null)
            {
                SplitPlanner.SetDialogError(resolveResult.Error ?? "无法生成拆分结果。");
                continue;
            }

            _splitSelectedFileNames = resolveResult.Value.FileNamesToMove.ToArray();
            SplitPlanner.ClearDialogError();
            break;
        }

        if (useManualPicker)
        {
            SamplePicker.Reset(gallery.Name);
            await LoadSamplePickerPageAsync();

            while (true)
            {
                var pickerResult = await _appDialogService.ShowAsync(
                    AppDialogKind.GallerySamplePicker,
                    SamplePicker,
                    new DialogOptions(Title: "拆分图库", PrimaryButtonText: "下一步"));
                if (pickerResult != AppDialogResult.Primary)
                {
                    _splitSourceGalleryId = null;
                    return;
                }

                if (SamplePicker.SelectedCount == 0)
                {
                    SamplePicker.SetDialogError("请至少选择一个样本。");
                    continue;
                }

                if (SamplePicker.SelectedCount >= gallery.SampleCount)
                {
                    SamplePicker.SetDialogError("拆分后源图库至少应保留 1 个样本。");
                    continue;
                }

                _splitSelectedFileNames = SamplePicker.SelectedFileNames.ToArray();
                SamplePicker.ClearDialogError();
                break;
            }
        }

        ReorganizeDialog.AvailableTargets.Clear();
        foreach (var target in _list.Galleries
                     .Where(item => item.Id != galleryId)
                     .OrderByDescending(item => item.SampleCount))
        {
            ReorganizeDialog.AvailableTargets.Add(
                new GalleryTargetOption(target.Id, target.Name, target.SampleCount));
        }

        ReorganizeDialog.Reset(
            $"{gallery.Name}-拆分",
            $"将把 {_splitSelectedFileNames.Length} 张样本拆分到目标图库。");

        while (true)
        {
            var targetResult = await _appDialogService.ShowAsync(
                AppDialogKind.GalleryMoveSamples,
                ReorganizeDialog,
                new DialogOptions(Title: "选择目标图库", PrimaryButtonText: "拆分"));
            if (targetResult != AppDialogResult.Primary)
            {
                _splitSourceGalleryId = null;
                return;
            }

            if (!CanSubmitGalleryReorganize)
            {
                ReorganizeDialog.SetDialogError(
                    ReorganizeDialog.UseNewGallery ? "请填写新图库名称。" : "请选择目标图库。");
                continue;
            }

            var splitResult = await _mediator.Send(new SplitGalleryCommand(
                galleryId,
                _splitSelectedFileNames,
                ReorganizeDialog.UseExistingGallery ? ReorganizeDialog.SelectedTargetGalleryId : null,
                ReorganizeDialog.UseNewGallery ? ReorganizeDialog.NewGalleryName.Trim() : null));

            _splitSourceGalleryId = null;
            if (splitResult.IsFailure || splitResult.Value is null)
            {
                ReorganizeDialog.SetDialogError(splitResult.Error ?? "拆分图库失败。");
                continue;
            }

            await _list.RefreshCurrentProjectGalleriesAsync();
            await _list.RecalculateProjectProgressAsync();
            var batch = splitResult.Value;
            var renameText = batch.Renamed > 0 ? $"，其中 {batch.Renamed} 个样本已自动重命名" : string.Empty;
            _snackbar.ShowSuccess($"已拆分 {batch.Moved} 张样本{renameText}。", "拆分图库");
            return;
        }
    }

    private bool CanRunGalleryOperation() => _list.CanRunGalleryOperation();

    private void NotifyBusyCommands()
    {
        ShowSplitGalleryDialogCommand.NotifyCanExecuteChanged();
        NotifyMergeCommandState();
    }

    private void NotifyMergeCommandState()
    {
        OnPropertyChanged(nameof(CanMergeGalleries));
        ShowMergeGalleriesDialogCommand.NotifyCanExecuteChanged();
    }

    private void OnSplitPlannerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_splitSourceGalleryId is null)
        {
            return;
        }

        if (e.PropertyName is nameof(GallerySplitPlannerState.SplitMode)
            or nameof(GallerySplitPlannerState.MoveRatio)
            or nameof(GallerySplitPlannerState.MoveRatioPercent)
            or nameof(GallerySplitPlannerState.MoveCount)
            or nameof(GallerySplitPlannerState.PreserveFileOrder)
            or nameof(GallerySplitPlannerState.RandomSeedText)
            or nameof(GallerySplitPlannerState.FileNamePrefixText)
            or nameof(GallerySplitPlannerState.SelectedLabel))
        {
            ShellAsync.FireAndForget(
                RefreshSplitPlannerPreviewAsync,
                _logger,
                "RefreshSplitPlannerPreview",
                ex => _snackbar.ShowError($"拆分预览失败：{ex.Message}", "图库拆分"));
        }
    }

    private void OnSamplePickerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GallerySamplePickerState.PageIndex)
            && _splitSourceGalleryId is not null)
        {
            ShellAsync.FireAndForget(
                LoadSamplePickerPageAsync,
                _logger,
                "LoadSamplePickerPage",
                ex => _snackbar.ShowError($"加载样本分页失败：{ex.Message}", "图库拆分"));
        }
    }

    private async Task RefreshSplitPlannerPreviewAsync()
    {
        if (_splitSourceGalleryId is null)
        {
            return;
        }

        CancelSplitPreview();
        var splitMode = SplitPlanner.ToSplitMode();
        if (splitMode == GallerySplitMode.Manual)
        {
            SplitPlanner.SetManualPreview();
            return;
        }

        if (!SplitPlanner.IsRandomSeedValid)
        {
            SplitPlanner.SetPreviewError("随机种子必须是整数。");
            return;
        }

        var previewCancellation = new CancellationTokenSource();
        _splitPreviewCancellation = previewCancellation;
        var previewVersion = ++_splitPreviewVersion;
        SplitPlanner.BeginPreview();
        try
        {
            await Task.Delay(150, previewCancellation.Token);
            var resolveResult = await _mediator.Send(
                BuildSplitSelectionQuery(_splitSourceGalleryId.Value),
                previewCancellation.Token);
            if (previewCancellation.IsCancellationRequested || previewVersion != _splitPreviewVersion)
            {
                return;
            }

            if (resolveResult.IsSuccess && resolveResult.Value is not null)
            {
                var preview = resolveResult.Value;
                SplitPlanner.SetPreview(preview.FileNamesToMove.Count, preview.TotalInGallery, preview.PreviewText);
                SplitPlanner.ClearDialogError();
                return;
            }

            SplitPlanner.SetPreviewError(resolveResult.Error ?? "无法预览拆分结果。");
        }
        catch (OperationCanceledException) when (previewCancellation.IsCancellationRequested)
        {
        }
    }

    private void CancelSplitPreview()
    {
        _splitPreviewCancellation?.Cancel();
        _splitPreviewCancellation?.Dispose();
        _splitPreviewCancellation = null;
    }

    private ResolveGallerySplitSelectionQuery BuildSplitSelectionQuery(Guid galleryId)
        => new(
            galleryId,
            SplitPlanner.ToSplitMode(),
            SplitPlanner.MoveRatio,
            SplitPlanner.ParseRandomSeed(),
            SplitPlanner.PreserveFileOrder,
            FileNamePrefix: SplitPlanner.GetFileNamePrefix(),
            TargetLabel: SplitPlanner.GetTargetLabel(),
            MoveCount: SplitPlanner.MoveCount);

    private async Task LoadSplitPlannerLabelsAsync(Guid galleryId)
    {
        var result = await _mediator.Send(new ListGalleryAnnotationLabelsQuery(galleryId));
        SplitPlanner.SetAvailableLabels(result.IsSuccess && result.Value is not null ? result.Value : []);
    }

    private async Task LoadSamplePickerPageAsync()
    {
        if (_splitSourceGalleryId is null)
        {
            return;
        }

        foreach (var row in _samplePickerRows)
        {
            row.SelectionChanged -= OnSamplePickerRowSelectionChanged;
        }

        _samplePickerRows.Clear();
        SamplePicker.PageItems.Clear();

        var pageResult = await _mediator.Send(new ListGallerySamplesQuery(
            _splitSourceGalleryId.Value,
            SamplePicker.PageIndex * GallerySamplePickerState.PageSize,
            GallerySamplePickerState.PageSize));
        if (pageResult.IsFailure || pageResult.Value is null)
        {
            _snackbar.ShowError(pageResult.Error ?? "加载样本分页失败。", "图库拆分");
            return;
        }

        var page = pageResult.Value;
        SamplePicker.TotalCount = page.TotalCount;
        foreach (var sample in page.Items)
        {
            var row = new GallerySamplePickerRow(
                sample.Id,
                sample.FileName,
                sample.IsAnnotated,
                SamplePicker.SelectedFileNames.Contains(sample.FileName));
            row.SelectionChanged += OnSamplePickerRowSelectionChanged;
            _samplePickerRows.Add(row);
            SamplePicker.PageItems.Add(row);
        }

        SamplePicker.NotifyStateChanged();
        SamplePicker.GoToPreviousPageCommand.NotifyCanExecuteChanged();
        SamplePicker.GoToNextPageCommand.NotifyCanExecuteChanged();
    }

    private void OnSamplePickerRowSelectionChanged(GallerySamplePickerRow row)
    {
        SamplePicker.ToggleSelection(row.FileName, row.IsSelected);
    }
}
