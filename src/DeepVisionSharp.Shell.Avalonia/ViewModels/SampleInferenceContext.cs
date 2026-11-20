using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeepVisionSharp.Presentation.ViewModels.Labeling;

namespace DeepVisionSharp.Shell.Avalonia.ViewModels;

/// <summary>
/// 推理页 stub 上下文：绑定路径与 deepvision 的 InferenceViewModel 同名同层
/// （Images / SelectedImage / ResultRows / BoxOverlays / StatusBar* 等），值全部是假数据，命令点了不做事。
/// 右侧检查器默认停在「推理结果」页签，好让结果行与检测明细在截图里可见。
/// </summary>
public sealed class SampleInferenceContext : ObservableObject
{
    private double _zoomScale = 1d;
    private int _selectedRightTabIndex = 1;
    private double _confidenceThreshold = 0.25;
    private int _maxDetections = 100;
    private bool _showLabelNames = true;
    private SampleInferenceImage? _selectedImage;
    private SampleInferenceModel? _selectedModel;
    private SampleInferenceDevice? _selectedDevice;
    private SampleInferenceDetectionRow? _selectedResultRow;

    public SampleInferenceContext()
    {
        Models =
        [
            new SampleInferenceModel
            {
                DisplayText = "钢板缺陷检测 yolo11n (YOLO 检测)",
                SpatialSummary = "整图 pad32 640×640 · 推理上限 1280",
                ShowsSpatialSummary = true,
            },
            new SampleInferenceModel
            {
                DisplayText = "电芯外观分类 yolo11s-cls (YOLO 分类)",
                SpatialSummary = "整图 224×224 · 不做 pad",
                ShowsSpatialSummary = true,
            },
        ];

        DeviceOptions =
        [
            new SampleInferenceDevice { DisplayName = "CPU" },
            new SampleInferenceDevice { DisplayName = "NVIDIA GeForce RTX 4060" },
        ];

        Images =
        [
            new SampleInferenceImage
            {
                DisplayName = "strip_20251118_0031.jpg",
                SourceLabel = "2025-11 冷轧线巡检",
                ExpectedLabelText = "预计NG",
                PredictedLabelText = "推理NG",
                EvaluationTag = "命中",
                HasPredicted = true,
                Preview = SampleInferenceArtwork.CreateThumbnail(Color.FromRgb(0x46, 0x53, 0x66), Color.FromRgb(0x9A, 0xA8, 0xB8)),
            },
            new SampleInferenceImage
            {
                DisplayName = "strip_20251118_0032.jpg",
                SourceLabel = "2025-11 冷轧线巡检",
                ExpectedLabelText = "预计OK",
                PredictedLabelText = "推理OK",
                EvaluationTag = "正确",
                HasPredicted = true,
                Preview = SampleInferenceArtwork.CreateThumbnail(Color.FromRgb(0x4C, 0x5A, 0x70), Color.FromRgb(0xA2, 0xB0, 0xC0)),
            },
            new SampleInferenceImage
            {
                DisplayName = "strip_20251118_0033.jpg",
                SourceLabel = "2025-11 冷轧线巡检",
                ExpectedLabelText = "预计NG",
                PredictedLabelText = "推理OK",
                EvaluationTag = "漏检",
                HasPredicted = true,
                Preview = SampleInferenceArtwork.CreateThumbnail(Color.FromRgb(0x53, 0x60, 0x76), Color.FromRgb(0xAC, 0xBA, 0xC9)),
            },
            new SampleInferenceImage
            {
                DisplayName = "strip_20251118_0034.jpg",
                SourceLabel = "2025-11 冷轧线巡检",
                ExpectedLabelText = "预计NG",
                PredictedLabelText = "推理—",
                EvaluationTag = string.Empty,
                Preview = SampleInferenceArtwork.CreateThumbnail(Color.FromRgb(0x5A, 0x67, 0x7D), Color.FromRgb(0xB6, 0xC4, 0xD2)),
            },
            new SampleInferenceImage
            {
                DisplayName = "cell_20251118_0107.jpg",
                SourceLabel = "外部导入",
                ExpectedLabelText = "预计OK",
                PredictedLabelText = "推理OK",
                EvaluationTag = "正确",
                HasPredicted = true,
                Preview = SampleInferenceArtwork.CreateThumbnail(Color.FromRgb(0x44, 0x5A, 0x60), Color.FromRgb(0x96, 0xB4, 0xB8)),
            },
            new SampleInferenceImage
            {
                DisplayName = "cell_20251118_0108.jpg",
                SourceLabel = "外部导入",
                ExpectedLabelText = "预计—",
                PredictedLabelText = "推理—",
                EvaluationTag = string.Empty,
                Preview = SampleInferenceArtwork.CreateThumbnail(Color.FromRgb(0x42, 0x52, 0x62), Color.FromRgb(0x92, 0xA6, 0xB4)),
            },
        ];

        _selectedImage = Images[0];
        _selectedModel = Models[0];
        _selectedDevice = DeviceOptions[1];

        ResultRows =
        [
            new SampleInferenceDetectionRow
            {
                ClassName = "划伤",
                BoxText = "(412, 168)  268 × 96",
                ConfidenceText = "0.94",
            },
            new SampleInferenceDetectionRow
            {
                ClassName = "凹坑",
                BoxText = "(724, 372)  148 × 132",
                ConfidenceText = "0.88",
            },
            new SampleInferenceDetectionRow
            {
                ClassName = "锈斑",
                BoxText = "(196, 566)  312 × 164",
                ConfidenceText = "0.71",
            },
        ];

        Canvas.BoxOverlays.Add(new BoxShapeViewModel("划伤", 412, 168, 268, 96));
        Canvas.BoxOverlays.Add(new BoxShapeViewModel("凹坑", 724, 372, 148, 132));
        Canvas.BoxOverlays.Add(new BoxShapeViewModel("锈斑", 196, 566, 312, 164));
        PreviewImage = SampleInferenceArtwork.CreatePreview();

        ShowImageSourceDialogCommand = new RelayCommand(() => { });
        RunSelectedCommand = new RelayCommand(() => { });
        RunAllCommand = new RelayCommand(() => { });
        ShowLastBatchReportCommand = new RelayCommand(() => { });
        StopCommand = new RelayCommand(() => { });
        SetExpectedOkCommand = new RelayCommand<SampleInferenceImage>(_ => { });
        SetExpectedNgCommand = new RelayCommand<SampleInferenceImage>(_ => { });
    }

    public string ProjectTitle => "冷轧带钢表面缺陷检测";

    public string ImageSourceSummary => "样本库：2025-11 冷轧线巡检（1,286 样本）";

    public bool HasProjectContext => true;

    public bool IsLoading => false;

    public bool IsRunning => false;

    public bool CanRun => true;

    public ObservableCollection<SampleInferenceImage> Images { get; }

    public int ImageCount => Images.Count;

    public SampleInferenceImage? SelectedImage
    {
        get => _selectedImage;
        set
        {
            if (SetProperty(ref _selectedImage, value))
            {
                OnPropertyChanged(nameof(SelectedImageSourceLabel));
                OnPropertyChanged(nameof(SelectedImageHasPredicted));
                OnPropertyChanged(nameof(SelectedImagePredictedLabelText));
                OnPropertyChanged(nameof(SelectedImageEvaluationTag));
            }
        }
    }

    public ObservableCollection<SampleInferenceModel> Models { get; }

    public SampleInferenceModel? SelectedModel
    {
        get => _selectedModel;
        set
        {
            if (SetProperty(ref _selectedModel, value))
            {
                OnPropertyChanged(nameof(ShowsInferenceDeviceSelector));
                OnPropertyChanged(nameof(SelectedModelDisplayText));
                OnPropertyChanged(nameof(SelectedModelSpatialSummary));
                OnPropertyChanged(nameof(ShowsSelectedModelSpatialSummary));
            }
        }
    }

    /// <summary>模型下拉的悬停提示（与真 VM 同口径的空安全投影）。</summary>
    public string SelectedModelDisplayText => SelectedModel?.DisplayText ?? string.Empty;

    public string SelectedModelSpatialSummary => SelectedModel?.SpatialSummary ?? string.Empty;

    public bool ShowsSelectedModelSpatialSummary => SelectedModel?.ShowsSpatialSummary ?? false;

    public bool ShowsInferenceDeviceSelector => SelectedModel is not null;

    public ObservableCollection<SampleInferenceDevice> DeviceOptions { get; }

    public SampleInferenceDevice? SelectedDevice
    {
        get => _selectedDevice;
        set => SetProperty(ref _selectedDevice, value);
    }

    public bool ShowsConfidenceThreshold => true;

    public double ConfidenceThreshold
    {
        get => _confidenceThreshold;
        set => SetProperty(ref _confidenceThreshold, value);
    }

    public bool ShowsMaxDetections => true;

    public int MaxDetections
    {
        get => _maxDetections;
        set => SetProperty(ref _maxDetections, value);
    }

    public int SelectedRightTabIndex
    {
        get => _selectedRightTabIndex;
        set => SetProperty(ref _selectedRightTabIndex, value);
    }

    /// <summary>画布图像：1280×800 的渐变占位图，检测框按这个像素尺寸摆放。</summary>
    public object? PreviewImage { get; }

    public bool HasPreviewImage => PreviewImage is not null;

    public string ActiveFileName => "strip_20251118_0031.jpg";

    public string ImageSizeText => "1280 × 800";

    public bool HasResult => true;

    public string ResultHeadline => "NG · 3 处缺陷";

    public string ResultDetail => "最高置信度 0.94（划伤）；耗时 42 ms，无后处理告警。";

    public bool HasEvaluationPairs => true;

    public string EvaluationSummaryText => "本会话 6 张：命中 2 · 正确 2 · 漏检 1 · 未判 1";

    public bool ShowResultRowList => true;

    public ObservableCollection<SampleInferenceDetectionRow> ResultRows { get; }

    public SampleInferenceDetectionRow? SelectedResultRow
    {
        get => _selectedResultRow;
        set => SetProperty(ref _selectedResultRow, value);
    }

    /// <summary>画布的叠加层宿主（对应真 VM 的 <c>Canvas</c> 子 VM）：推理页只读画布只读这一路。</summary>
    public SampleInferenceCanvas Canvas { get; } = new();

    /// <summary>当前样本来源标签（与真 VM 同口径的空安全投影）。</summary>
    public string SelectedImageSourceLabel => SelectedImage?.SourceLabel ?? string.Empty;

    public bool SelectedImageHasPredicted => SelectedImage?.HasPredicted ?? false;

    public string SelectedImagePredictedLabelText => SelectedImage?.PredictedLabelText ?? string.Empty;

    public string SelectedImageEvaluationTag => SelectedImage?.EvaluationTag ?? string.Empty;

    public bool ShowLabelNames
    {
        get => _showLabelNames;
        set => SetProperty(ref _showLabelNames, value);
    }

    public double ZoomScale
    {
        get => _zoomScale;
        set
        {
            if (SetProperty(ref _zoomScale, value))
            {
                OnPropertyChanged(nameof(ZoomText));
            }
        }
    }

    public string ZoomText => $"{Math.Round(ZoomScale * 100)}%";

    public string StatusBarState => "完成";

    public string StatusBarPerfText => "预检 8 ms · 加载 61 ms · 推理 42 ms · 单张 1280×800";

    public string StatusBarDeviceText => "NVIDIA GeForce RTX 4060 · cuda:0";

    public ICommand ShowImageSourceDialogCommand { get; }

    public ICommand RunSelectedCommand { get; }

    public ICommand RunAllCommand { get; }

    public ICommand ShowLastBatchReportCommand { get; }

    public ICommand StopCommand { get; }

    public ICommand SetExpectedOkCommand { get; }

    public ICommand SetExpectedNgCommand { get; }
}

/// <summary>待推理列表项 stub（对应 InferenceImageItem）：缩略图是画出来的渐变占位图。</summary>
public sealed class SampleInferenceImage
{
    public required string DisplayName { get; init; }

    public required string SourceLabel { get; init; }

    public required string ExpectedLabelText { get; init; }

    public required string PredictedLabelText { get; init; }

    /// <summary>命中 / 正确 / 漏检 / 误报，空串表示未判定；页面的 evalTag 类名靠这四个值匹配。</summary>
    public required string EvaluationTag { get; init; }

    public bool HasPredicted { get; init; }

    public IImage? Preview { get; init; }
}

/// <summary>画布叠加层宿主 stub（对应 InferenceCanvasViewModel）：推理页只读这一路。</summary>
public sealed class SampleInferenceCanvas
{
    public ObservableCollection<BoxShapeViewModel> BoxOverlays { get; } = [];
}

/// <summary>模型下拉项 stub（对应 InferencePageModelOption）。</summary>
public sealed class SampleInferenceModel
{
    public required string DisplayText { get; init; }

    public required string SpatialSummary { get; init; }

    public bool ShowsSpatialSummary { get; init; }
}

/// <summary>推理设备下拉项 stub（对应 InferenceDeviceOption）。</summary>
public sealed class SampleInferenceDevice
{
    public required string DisplayName { get; init; }
}

/// <summary>检测明细行 stub（对应 InferenceDetectionRow）：类别 + 框坐标 + 置信度。</summary>
public sealed class SampleInferenceDetectionRow
{
    public required string ClassName { get; init; }

    public required string BoxText { get; init; }

    public required string ConfidenceText { get; init; }
}

/// <summary>stub 用的占位图：渐变矩形，没有真实图像文件。</summary>
internal static class SampleInferenceArtwork
{
    public static IImage CreateThumbnail(Color from, Color to) => CreateGradient(from, to, 40, 40);

    public static IImage CreatePreview() => CreateGradient(
        Color.FromRgb(0x3E, 0x4B, 0x5E),
        Color.FromRgb(0xB4, 0xC2, 0xD1),
        1280,
        800);

    private static IImage CreateGradient(Color from, Color to, double width, double height)
        => new DrawingImage(new GeometryDrawing
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
            Geometry = new RectangleGeometry(new Rect(0, 0, width, height)),
        });
}
