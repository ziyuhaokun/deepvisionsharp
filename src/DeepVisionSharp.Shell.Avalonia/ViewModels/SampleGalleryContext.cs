using System.Windows.Input;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DeepVisionSharp.Shell.Avalonia.ViewModels;

/// <summary>
/// 图库页 stub 上下文：绑定路径与 deepvision 的 GalleryViewModel 同名同层
/// （List / Editor / Transfer / Reorganize / BatchInference / CardColumns），值全部是假数据，命令点了不做事。
/// </summary>
public sealed class SampleGalleryContext : ObservableObject
{
    private int _cardColumns = 3;

    public SampleGalleryList List { get; } = new();

    public SampleGalleryEditor Editor { get; } = new();

    public SampleGalleryTransfer Transfer { get; } = new();

    public SampleGalleryReorganize Reorganize { get; } = new();

    public SampleGalleryBatchInference BatchInference { get; } = new();

    /// <summary>卡片栏数：由页面按宽度算出来回写（deepvision 里在 GalleryViewModel 上）。</summary>
    public int CardColumns
    {
        get => _cardColumns;
        set => SetProperty(ref _cardColumns, value);
    }
}

/// <summary>图库列表 stub（对应 GalleryListViewModel）。</summary>
public sealed class SampleGalleryList : ObservableObject
{
    private string _keyword = string.Empty;
    private bool _isGallerySelectionMode;

    public SampleGalleryList()
    {
        FilteredGalleries =
        [
            new SampleGallery
            {
                Id = "sample-gallery-1",
                Name = "2025-11 冷轧线巡检",
                Description = "本月冷轧线巡检图像，按框标注划伤、凹坑、锈斑三类缺陷。",
                Thumbnail = SampleGalleryThumbnails.Create(Color.FromRgb(0x35, 0x4A, 0x63), Color.FromRgb(0x8F, 0xA6, 0xBF)),
                SampleCountText = "1,286 样本",
                AnnotatedCountText = "1,104 已标注",
                Progress = 0.86,
                ProgressText = 0.86.ToString("P0"),
                LastUpdatedText = "12 分钟前",
                CanDelete = true,
            },
            new SampleGallery
            {
                Id = "sample-gallery-2",
                Name = "电芯外观分选",
                Description = "产线分选工位的整图样本，OK/NG 由图像级标注给出。",
                Thumbnail = SampleGalleryThumbnails.Create(Color.FromRgb(0x63, 0x4A, 0x35), Color.FromRgb(0xC2, 0xA6, 0x7E)),
                SampleCountText = "2,048 样本",
                AnnotatedCountText = "2,048 已标注",
                Progress = 1,
                ProgressText = 1d.ToString("P0"),
                LastUpdatedText = "3 小时前",
                CanDelete = true,
            },
            new SampleGallery
            {
                Id = "sample-gallery-3",
                Name = "注塑件良品采样",
                Description = "无监督异常检测的良品集，暂不标注，仅用于建立分布基线。",
                Thumbnail = SampleGalleryThumbnails.Create(Color.FromRgb(0x3F, 0x5C, 0x50), Color.FromRgb(0x9C, 0xBE, 0xAD)),
                SampleCountText = "980 样本",
                AnnotatedCountText = "0 已标注",
                Progress = 0,
                ProgressText = 0d.ToString("P0"),
                LastUpdatedText = "2 天前",
                CanDelete = true,
            },
            new SampleGallery
            {
                Id = "sample-gallery-4",
                Name = "瓶盖字符待核对",
                Description = "字符区域检测数据，PPOCRLabel 导入后仍需人工核对少量文本框。",
                Thumbnail = SampleGalleryThumbnails.Create(Color.FromRgb(0x4B, 0x3C, 0x63), Color.FromRgb(0xAE, 0x9C, 0xC9)),
                SampleCountText = "1,140 样本",
                AnnotatedCountText = "986 已标注",
                Progress = 0.865,
                ProgressText = 0.865.ToString("P0"),
                LastUpdatedText = "5 天前",
                CanDelete = true,
            },
            new SampleGallery
            {
                Id = "sample-gallery-5",
                Name = "异常检测 · 训练良品",
                Description = "系统自动维护的训练良品集，随训练任务生成，不可编辑与删除。",
                Thumbnail = null,
                IsSystem = true,
                SystemKindBadge = "训练",
                SampleCountText = "640 样本",
                AnnotatedCountText = "640 已标注",
                Progress = 1,
                ProgressText = 1d.ToString("P0"),
                LastUpdatedText = "8 天前",
                CanDelete = false,
            },
        ];

        ClearSearchCommand = new RelayCommand(() => { });
        ShowProjectLabelStatisticsDialogCommand = new RelayCommand(() => { });
        ToggleGallerySelectionModeCommand = new RelayCommand(() => { });
        SelectAllGalleriesCommand = new RelayCommand(() => { });
        DeselectAllGalleriesCommand = new RelayCommand(() => { });
        EnterGalleryCommand = new RelayCommand(() => { });
        OpenGalleryFolderCommand = new RelayCommand(() => { });
        ShowGalleryLabelStatisticsDialogCommand = new RelayCommand(() => { });
    }

    public string OverviewSummary => "5 个图库 · 6,094 个样本 · 4,778 个已标注";

    public string Keyword
    {
        get => _keyword;
        set => SetProperty(ref _keyword, value);
    }

    public bool IsGallerySelectionMode
    {
        get => _isGallerySelectionMode;
        set => SetProperty(ref _isGallerySelectionMode, value);
    }

    public int SelectedGalleryCount => FilteredGalleries.Count(gallery => gallery.IsSelected);

    public IReadOnlyList<SampleGallery> FilteredGalleries { get; }

    public bool HasGalleries => true;

    public bool HasFilteredGalleries => true;

    public bool HasEmptyState => !HasFilteredGalleries;

    public string EmptyStateTitle => "暂无图库";

    public string EmptyStateDescription => "导入一个图库，或在当前项目里新建一个空图库开始标注";

    public bool ShowsGalleryLabelStatistics => true;

    public bool ShowsGalleryEdit => true;

    public bool ShowsGalleryBatchInference => true;

    public bool IsTextRecognitionProject => false;

    public bool ShowsGallerySplit => true;

    public ICommand ClearSearchCommand { get; }

    public ICommand ShowProjectLabelStatisticsDialogCommand { get; }

    public ICommand ToggleGallerySelectionModeCommand { get; }

    public ICommand SelectAllGalleriesCommand { get; }

    public ICommand DeselectAllGalleriesCommand { get; }

    public ICommand EnterGalleryCommand { get; }

    public ICommand OpenGalleryFolderCommand { get; }

    public ICommand ShowGalleryLabelStatisticsDialogCommand { get; }
}

/// <summary>图库编辑 stub（对应 GalleryEditorViewModel）。</summary>
public sealed class SampleGalleryEditor
{
    public SampleGalleryEditor()
    {
        ShowCreateGalleryDialogCommand = new RelayCommand(() => { });
        ShowEditGalleryDialogCommand = new RelayCommand(() => { });
        ConfirmClearAnnotationsCommand = new RelayCommand(() => { });
        ConfirmDeleteGalleryCommand = new RelayCommand(() => { });
    }

    public bool IsSavingGallery => false;

    public ICommand ShowCreateGalleryDialogCommand { get; }

    public ICommand ShowEditGalleryDialogCommand { get; }

    public ICommand ConfirmClearAnnotationsCommand { get; }

    public ICommand ConfirmDeleteGalleryCommand { get; }
}

/// <summary>图库导入 / 导出 stub（对应 GalleryTransferViewModel）。</summary>
public sealed class SampleGalleryTransfer
{
    public SampleGalleryTransfer()
    {
        ImportGalleryCommand = new RelayCommand(() => { });
        ImportImagesCommand = new RelayCommand(() => { });
        ImportFolderCommand = new RelayCommand(() => { });
        ExportGalleryCommand = new RelayCommand(() => { });
        ImportPpOcrSkipCommand = new RelayCommand(() => { });
        ImportPpOcrAppendCommand = new RelayCommand(() => { });
        ImportPpOcrReplaceCommand = new RelayCommand(() => { });
        ExportPpOcrLabelsCommand = new RelayCommand(() => { });
    }

    public bool IsImporting => false;

    public bool IsNotImporting => !IsImporting;

    public string ImportStage => "正在导入图库";

    public string ImportCurrentFile => string.Empty;

    public double ImportProgressValue => 0;

    public string ImportProgressText => "0 / 0 个文件";

    public ICommand ImportGalleryCommand { get; }

    public ICommand ImportImagesCommand { get; }

    public ICommand ImportFolderCommand { get; }

    public ICommand ExportGalleryCommand { get; }

    public ICommand ImportPpOcrSkipCommand { get; }

    public ICommand ImportPpOcrAppendCommand { get; }

    public ICommand ImportPpOcrReplaceCommand { get; }

    public ICommand ExportPpOcrLabelsCommand { get; }
}

/// <summary>图库整理 stub（对应 GalleryReorganizeViewModel）。</summary>
public sealed class SampleGalleryReorganize
{
    public SampleGalleryReorganize()
    {
        ShowMergeGalleriesDialogCommand = new RelayCommand(() => { });
        ShowSplitGalleryDialogCommand = new RelayCommand(() => { });
    }

    public bool CanMergeGalleries => false;

    public ICommand ShowMergeGalleriesDialogCommand { get; }

    public ICommand ShowSplitGalleryDialogCommand { get; }
}

/// <summary>图库批推理 stub（对应 GalleryBatchInferenceViewModel）。</summary>
public sealed class SampleGalleryBatchInference
{
    public SampleGalleryBatchInference()
    {
        ShowDialogCommand = new RelayCommand(() => { });
    }

    public ICommand ShowDialogCommand { get; }
}

/// <summary>占位缩略图：160×120 的对角渐变矩形，够让卡片走到「有缩略图」那条分支。</summary>
internal static class SampleGalleryThumbnails
{
    public static IImage Create(Color from, Color to) => new DrawingImage(new GeometryDrawing
    {
        Brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(from, 0),
                new GradientStop(to, 1),
            },
        },
        Geometry = new RectangleGeometry(new Rect(0, 0, 160, 120)),
    });
}

/// <summary>图库卡片 stub（对应 GalleryRow）：缩略图是画出来的渐变占位图，没有真实图像。</summary>
public sealed class SampleGallery : ObservableObject
{
    private bool _isSelected;

    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string Description { get; init; }

    public required string SampleCountText { get; init; }

    public required string AnnotatedCountText { get; init; }

    public required double Progress { get; init; }

    public required string ProgressText { get; init; }

    public required string LastUpdatedText { get; init; }

    public bool IsSystem { get; init; }

    public string? SystemKindBadge { get; init; }

    public bool CanDelete { get; init; }

    public IImage? Thumbnail { get; init; }

    public bool HasThumbnail => Thumbnail is not null;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
