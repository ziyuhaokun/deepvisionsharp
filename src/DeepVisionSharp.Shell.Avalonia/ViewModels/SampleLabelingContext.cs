using System.Runtime.InteropServices;
using System.Windows.Input;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeepVisionSharp.Presentation.ViewModels.Labeling;

namespace DeepVisionSharp.Shell.Avalonia.ViewModels;

/// <summary>
/// 标记页 stub 上下文：绑定路径与真实的 <see cref="LabelingViewModel"/> 同名同层
/// （Workspace / Editor / Labels / Samples / Inference / ActiveRailKind），值全部是造出来的假数据，命令点了不做事。
/// 只在没有容器（设计时 / 无头渲染）时兜底；画布与框列表直接复用 Presentation 的
/// <see cref="BoxShapeViewModel"/>，不再另起一套行类型。
/// </summary>
public sealed class SampleLabelingContext : ObservableObject
{
    private string _activeRailKind = "Detection";

    public SampleLabelingWorkspace Workspace { get; } = new();

    public SampleLabelingEditor Editor { get; } = new();

    public SampleLabelingLabels Labels { get; } = new();

    public SampleLabelingSamples Samples { get; } = new();

    public SampleLabelingInference Inference { get; } = new();

    public SampleLabelingContext()
    {
        BackToGalleryCommand = new RelayCommand(() => { });
    }

    /// <summary>左侧轨道的枚举名（Detection / Classification）：本仓库只有这两种工作流。</summary>
    public string ActiveRailKind
    {
        get => _activeRailKind;
        set => SetProperty(ref _activeRailKind, value);
    }

    public ICommand BackToGalleryCommand { get; }

    /// <summary>框列表点行时的选中入口，与真实 VM 的 SelectBoxCommand 同一动作。</summary>
    internal void SelectBox(BoxShapeViewModel box) => Editor.Select(box);
}

/// <summary>标注工作区 stub（对应 LabelingWorkspaceContext）。</summary>
public sealed class SampleLabelingWorkspace
{
    public SampleLabelingWorkspace()
    {
        ImageSource = SampleLabelingImages.CreateScene(1280, 860);
    }

    public string ActiveFileName => "cold-roll_20251118_004317.jpg";

    public string ImageSizeText => "1280 × 860";

    public IImage ImageSource { get; }

    public string GalleryProgressText => "已标注 86 / 120";

    public SampleLabelingProjectProfile ProjectProfile { get; } = new();

    public SampleLabelingUiPolicy UiPolicy { get; } = new();
}

/// <summary>项目画像 stub（对应 ProjectCapabilityProfile）：决定页面上哪些工具可见。</summary>
public sealed class SampleLabelingProjectProfile
{
    public bool SupportsClassLabels => true;

    public bool SupportsImageClass => false;
}

/// <summary>界面策略 stub（对应 LabelingUiPolicy）。</summary>
public sealed class SampleLabelingUiPolicy
{
    public bool CanClearAllAnnotations => true;

    public bool CanUseAutoFit => true;

    public bool CanUseRectangleTool => true;

    public bool ShowsAnnotationModeTools => true;
}

/// <summary>标注编辑器 stub（对应 LabelingEditorViewModel）：画布与框列表的框都从这里出。</summary>
public sealed class SampleLabelingEditor : ObservableObject
{
    private bool _showLabelNames = true;
    private double _zoomScale = 1;
    private BoxShapeViewModel? _selectedBox;
    private BoxToolMode _activeToolMode = BoxToolMode.Draw;

    public SampleLabelingEditor()
    {
        Boxes =
        [
            new BoxShapeViewModel("划伤", 212, 168, 318, 132) { Number = 1 },
            new BoxShapeViewModel("凹坑", 612, 246, 196, 164) { Number = 2 },
            new BoxShapeViewModel("锈斑", 884, 470, 244, 168) { Number = 3 },
            new BoxShapeViewModel("划伤", 348, 588, 286, 96) { Number = 4 },
        ];

        Select(Boxes[0]);

        ClearBoxesCommand = new RelayCommand(() => { });
        DeleteBoxCommand = new RelayCommand<BoxShapeViewModel>(_ => { });
        SelectBoxCommand = new RelayCommand<BoxShapeViewModel>(_ => { });
        SetRectangleToolCommand = new RelayCommand(() => { });
        SetSelectToolCommand = new RelayCommand(() => { });
        UndoCommand = new RelayCommand(() => { });
        RedoCommand = new RelayCommand(() => { });
    }

    public IReadOnlyList<BoxShapeViewModel> Boxes { get; }

    public bool HasBoxes => Boxes.Count > 0;

    public bool HasSelectedBox => SelectedBox is not null;

    public bool CanUseEditor => true;

    public bool IsNotBusy => true;

    public bool CanUndo => true;

    public bool CanRedo => false;

    public BoxToolMode ActiveToolMode => _activeToolMode;

    public bool IsSelectTool => ActiveToolMode == BoxToolMode.Select;

    public bool IsRectangleTool => ActiveToolMode == BoxToolMode.Draw;

    public BoxShapeViewModel? SelectedBox => _selectedBox;

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

    public string ZoomText => ZoomScale.ToString("P0");

    public ICommand ClearBoxesCommand { get; }

    public ICommand DeleteBoxCommand { get; }

    public ICommand SelectBoxCommand { get; }

    public ICommand SetRectangleToolCommand { get; }

    public ICommand SetSelectToolCommand { get; }

    public ICommand UndoCommand { get; }

    public ICommand RedoCommand { get; }

    /// <summary>与真实编辑器的选中语义一致：同时只允许一个框处于选中态。</summary>
    internal void Select(BoxShapeViewModel box)
    {
        foreach (var item in Boxes)
        {
            item.IsSelected = ReferenceEquals(item, box);
        }

        _selectedBox = box;
        OnPropertyChanged(nameof(SelectedBox));
        OnPropertyChanged(nameof(HasSelectedBox));
    }
}

/// <summary>类别列表 stub（对应 LabelingLabelsViewModel）。</summary>
public sealed class SampleLabelingLabels
{
    public SampleLabelingLabels()
    {
        LabelOptions =
        [
            new SampleLabelOption { Id = "label-1", Name = "划伤", HotkeyText = "1", Color = "#F59E0B" },
            new SampleLabelOption { Id = "label-2", Name = "凹坑", HotkeyText = "2", Color = "#3B82F6" },
            new SampleLabelOption { Id = "label-3", Name = "锈斑", HotkeyText = "3", Color = "#EF4444" },
            new SampleLabelOption { Id = "label-4", Name = "压痕", HotkeyText = "4", Color = "#22C55E" },
        ];

        ActiveLabelId = LabelOptions[0].Id;
        ActiveLabelName = LabelOptions[0].Name;
        ActiveLabelColor = LabelOptions[0].Color;
        SelectedLabel = LabelOptions[0];

        DeleteLabelCommand = new RelayCommand<SampleLabelOption>(_ => { });
        SelectLabelCommand = new RelayCommand<SampleLabelOption>(_ => { });
        ShowAddLabelDialogCommand = new RelayCommand(() => { });
        ShowEditLabelColorDialogCommand = new RelayCommand<SampleLabelOption>(_ => { });
    }

    public IReadOnlyList<SampleLabelOption> LabelOptions { get; }

    public string ActiveLabelId { get; }

    public string ActiveLabelName { get; }

    public string ActiveLabelColor { get; }

    public SampleLabelOption? SelectedLabel { get; }

    public bool CanManageLabels => true;

    public ICommand DeleteLabelCommand { get; }

    public ICommand SelectLabelCommand { get; }

    public ICommand ShowAddLabelDialogCommand { get; }

    public ICommand ShowEditLabelColorDialogCommand { get; }
}

/// <summary>一个类别（对应 LabelOptionRow）：颜色是 #RRGGBB，由控件侧解析。</summary>
public sealed class SampleLabelOption
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string HotkeyText { get; init; }

    public required string Color { get; init; }
}

/// <summary>样本条 stub（对应 LabelingSampleListViewModel）：胶片条上的 5 张缩略图从这里出。</summary>
public sealed class SampleLabelingSamples : ObservableObject
{
    private bool _isSamplePanelExpanded = true;

    public SampleLabelingSamples()
    {
        SamplePageItems =
        [
            new SampleLabelingSampleRow
            {
                Id = "sample-1",
                Index = 1,
                FileName = "cold-roll_004315.jpg",
                Thumbnail = SampleLabelingImages.CreateThumbnail(
                    SampleLabelingPalette.ThumbFrom, SampleLabelingPalette.ThumbTo),
                IsActive = true,
                IsAnnotated = true,
                IsAnchor = true,
            },
            new SampleLabelingSampleRow
            {
                Id = "sample-2",
                Index = 2,
                FileName = "cold-roll_004316.jpg",
                Thumbnail = SampleLabelingImages.CreateThumbnail(
                    SampleLabelingPalette.ThumbFrom, SampleLabelingPalette.ThumbTo),
                IsAnnotated = true,
            },
            new SampleLabelingSampleRow
            {
                Id = "sample-3",
                Index = 3,
                FileName = "cold-roll_004317.jpg",
                Thumbnail = SampleLabelingImages.CreateThumbnail(
                    SampleLabelingPalette.ThumbFrom, SampleLabelingPalette.ThumbTo),
                IsAnnotated = true,
            },
            new SampleLabelingSampleRow
            {
                Id = "sample-4",
                Index = 4,
                FileName = "cold-roll_004318.jpg",
                Thumbnail = SampleLabelingImages.CreateThumbnail(
                    SampleLabelingPalette.ThumbFrom, SampleLabelingPalette.ThumbTo),
                HasError = true,
            },
            new SampleLabelingSampleRow
            {
                Id = "sample-5",
                Index = 5,
                FileName = "cold-roll_004319.jpg",
                Thumbnail = SampleLabelingImages.CreateThumbnail(
                    SampleLabelingPalette.ThumbFrom, SampleLabelingPalette.ThumbTo),
                IsAnnotated = true,
            },
        ];

        ImportImagesCommand = new RelayCommand(() => { });
        GoToNextSamplePageCommand = new RelayCommand(() => { });
        GoToPreviousSamplePageCommand = new RelayCommand(() => { });
        NavigateNextSampleCommand = new RelayCommand(() => { });
        NavigatePreviousSampleCommand = new RelayCommand(() => { });
        ShowMoveSamplesDialogCommand = new RelayCommand(() => { });
        ToggleSamplePanelCommand = new RelayCommand(() => { });
    }

    public IReadOnlyList<SampleLabelingSampleRow> SamplePageItems { get; }

    public bool HasSamples => true;

    public bool HasNoSamples => false;

    public string ActiveSamplePositionText => "3 / 120";

    public string SamplePageText => "1 / 24";

    public string SamplePanelToggleTooltip => "收起样本列表";

    public bool CanImportImages => true;

    public bool CanMoveSelectedSamples => true;

    public bool IsSamplePanelExpanded
    {
        get => _isSamplePanelExpanded;
        set
        {
            if (SetProperty(ref _isSamplePanelExpanded, value))
            {
                OnPropertyChanged(nameof(SamplePanelToggleTooltip));
            }
        }
    }

    public ICommand ImportImagesCommand { get; }

    public ICommand GoToNextSamplePageCommand { get; }

    public ICommand GoToPreviousSamplePageCommand { get; }

    public ICommand NavigateNextSampleCommand { get; }

    public ICommand NavigatePreviousSampleCommand { get; }

    public ICommand ShowMoveSamplesDialogCommand { get; }

    public ICommand ToggleSamplePanelCommand { get; }
}

/// <summary>胶片条上的一张样本（对应 LabelingSampleRow）。</summary>
public sealed class SampleLabelingSampleRow : ObservableObject
{
    private bool _isActive;
    private bool _isSelected;

    public required string Id { get; init; }

    public required int Index { get; init; }

    public required string FileName { get; init; }

    public IImage? Thumbnail { get; init; }

    public bool IsAnchor { get; init; }

    public bool IsAnnotated { get; init; }

    public bool HasError { get; init; }

    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

/// <summary>自动推理 stub（对应 LabelingInferenceViewModel）。</summary>
public sealed class SampleLabelingInference
{
    public SampleLabelingInference()
    {
        RunAutoInferenceCommand = new RelayCommand(() => { });
        RunInferenceDebugCommand = new RelayCommand(() => { });
        ShowAutoInferenceDialogCommand = new RelayCommand(() => { });
    }

    public bool IsVisible => true;

    public bool CanOpenInferenceDialog => true;

    public string InferenceActionLabel => "自动推理";

    public string InferenceButtonToolTip => "用当前模型自动标注这张图";

    public ICommand RunAutoInferenceCommand { get; }

    public ICommand RunInferenceDebugCommand { get; }

    public ICommand ShowAutoInferenceDialogCommand { get; }
}

/// <summary>stub 用色板与占位图：缩略图是画出来的渐变，画布图像是写出来的像素。</summary>
internal static class SampleLabelingPalette
{
    public static readonly Color ThumbFrom = Color.FromRgb(0x33, 0x41, 0x55);

    public static readonly Color ThumbTo = Color.FromRgb(0x8B, 0x9C, 0xB0);
}

/// <summary>占位图像：缩略图走渐变矩形，画布图像走真实的 WriteableBitmap 像素。</summary>
internal static class SampleLabelingImages
{
    public static IImage CreateThumbnail(Color from, Color to) => new DrawingImage(new GeometryDrawing
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

    /// <summary>
    /// 造一张「产线工件照」：冷灰渐变底 + 一条亮色带 + 三个螺栓孔 + 两道划痕。
    /// 画布控件按 Bitmap.PixelSize 做适配缩放，所以必须是真实位图而不是 DrawingImage。
    /// </summary>
    public static WriteableBitmap CreateScene(int width, int height)
    {
        var bitmap = new WriteableBitmap(
            new PixelSize(width, height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Opaque);

        var stride = width * 4;
        var buffer = new byte[stride * height];

        for (var y = 0; y < height; y++)
        {
            var row = y * stride;
            var vertical = y / (double)height;
            for (var x = 0; x < width; x++)
            {
                var horizontal = x / (double)width;
                var shade = 0.30 + 0.34 * vertical + 0.18 * Math.Sin(horizontal * Math.PI);

                // 工件的亮色带（水平中线附近）
                var bandDistance = Math.Abs(vertical - 0.46);
                if (bandDistance < 0.13)
                {
                    shade += 0.22 * (1 - bandDistance / 0.13);
                }

                var b = (byte)Math.Clamp(255 * (shade * 0.86), 0, 255);
                var g = (byte)Math.Clamp(255 * (shade * 0.92), 0, 255);
                var r = (byte)Math.Clamp(255 * shade, 0, 255);

                // 两道划痕：从左上到右下的斜线
                if (Math.Abs(x - y * 1.4 - width * 0.18) < 3 || Math.Abs(x - y * 1.4 - width * 0.62) < 2)
                {
                    r = (byte)Math.Min(255, r + 26);
                    g = (byte)Math.Min(255, g + 26);
                    b = (byte)Math.Min(255, b + 26);
                }

                var offset = row + x * 4;
                buffer[offset] = b;
                buffer[offset + 1] = g;
                buffer[offset + 2] = r;
                buffer[offset + 3] = 255;
            }
        }

        DrawBoltHoles(buffer, stride, width, height);

        using (var frameBuffer = bitmap.Lock())
        {
            Marshal.Copy(buffer, 0, frameBuffer.Address, buffer.Length);
        }

        return bitmap;
    }

    private static void DrawBoltHoles(byte[] buffer, int stride, int width, int height)
    {
        Span<(double X, double Y)> centers =
        [
            (width * 0.16, height * 0.20),
            (width * 0.82, height * 0.24),
            (width * 0.34, height * 0.78),
        ];

        const double Radius = 34;
        foreach (var (cx, cy) in centers)
        {
            var left = (int)(cx - Radius);
            var right = (int)(cx + Radius);
            var top = (int)(cy - Radius);
            var bottom = (int)(cy + Radius);
            for (var y = Math.Max(0, top); y < Math.Min(height, bottom); y++)
            {
                for (var x = Math.Max(0, left); x < Math.Min(width, right); x++)
                {
                    var dx = x - cx;
                    var dy = y - cy;
                    var distance = Math.Sqrt((dx * dx) + (dy * dy));
                    if (distance > Radius)
                    {
                        continue;
                    }

                    var ring = distance > Radius - 5;
                    var value = ring ? (byte)210 : (byte)36;
                    var offset = (y * stride) + (x * 4);
                    buffer[offset] = value;
                    buffer[offset + 1] = value;
                    buffer[offset + 2] = value;
                }
            }
        }
    }
}