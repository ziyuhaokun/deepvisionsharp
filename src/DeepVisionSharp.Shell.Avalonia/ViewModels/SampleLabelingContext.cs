using System.Runtime.InteropServices;
using System.Windows.Input;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DeepVisionSharp.Shell.Avalonia.ViewModels;

/// <summary>
/// 标记页 stub 上下文：绑定路径与 deepvision 的 LabelingViewModel 同名同层
/// （Workspace / Editor / Labels / Samples / AiJudge / TemplateMatch / Inference / ActiveRailKind），
/// 值全部是造出来的假数据，命令点了不做事。
/// </summary>
public sealed class SampleLabelingContext : ObservableObject
{
    private string _activeRailKind = "Detection";

    public SampleLabelingWorkspace Workspace { get; } = new();

    public SampleLabelingEditor Editor { get; } = new();

    public SampleLabelingLabels Labels { get; } = new();

    public SampleLabelingSamples Samples { get; } = new();

    public SampleLabelingAiJudge AiJudge { get; } = new();

    public SampleTemplateMatch TemplateMatch { get; } = new();

    public SampleLabelingInference Inference { get; } = new();

    public SampleLabelingContext()
    {
        BackToGalleryCommand = new RelayCommand(() => { });
    }

    /// <summary>左侧轨道的枚举名（Detection / TextRecognition / Classification / Segmentation / Variation / Anomaly）。</summary>
    public string ActiveRailKind
    {
        get => _activeRailKind;
        set => SetProperty(ref _activeRailKind, value);
    }

    public bool ShowAnnotationModeTools => true;

    public bool ShowAnomalySystemMoveTools => false;

    public string ReviewedBackgroundButtonText => "标记纯背景";

    public ICommand BackToGalleryCommand { get; }
}

/// <summary>标注工作区 stub（对应 LabelingWorkspaceViewModel）。</summary>
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

/// <summary>项目画像 stub（对应 ProjectProfile）：决定页面上哪些工具可见。</summary>
public sealed class SampleLabelingProjectProfile
{
    public bool RequiresInstanceIdOnPolygons => false;

    public bool SupportsClassLabels => true;

    public bool SupportsImageClass => false;

    public bool SupportsMasks => true;

    public bool SupportsTextRecognition => false;
}

/// <summary>界面策略 stub（对应 LabelingUiPolicy）。</summary>
public sealed class SampleLabelingUiPolicy
{
    public bool CanClearAllAnnotations => true;

    public bool CanDeleteGeometry => true;

    public bool CanUseAutoFit => true;

    public bool CanUsePolygonTool => true;

    public bool CanUseRectangleTool => true;

    public bool ShowsOkNgShortcuts => false;
}

/// <summary>画布工具模式：与 deepvision 的 LabelingToolMode 同名同值。</summary>
public enum LabelingToolMode
{
    Select = 0,
    Rectangle = 1,
    Polygon = 2
}

/// <summary>标注编辑器 stub（对应 LabelingEditorViewModel）：画布上的 4 个框与多边形列表都从这里出。</summary>
public sealed class SampleLabelingEditor : ObservableObject
{
    private bool _showLabelNames = true;
    private double _zoomScale = 1;
    private bool _ocrConfirmed = true;

    public SampleLabelingEditor()
    {
        AnnotationRows =
        [
            new SampleAnnotationRow
            {
                Id = "ann-1",
                Label = "划伤",
                Color = SampleLabelingPalette.Brush(SampleLabelingPalette.LabelColors[0]),
                Bounds = new Rect(212, 168, 318, 132),
                IsSelected = true,
            },
            new SampleAnnotationRow
            {
                Id = "ann-2",
                Label = "凹坑",
                Color = SampleLabelingPalette.Brush(SampleLabelingPalette.LabelColors[1]),
                Bounds = new Rect(612, 246, 196, 164),
            },
            new SampleAnnotationRow
            {
                Id = "ann-3",
                Label = "锈斑",
                Color = SampleLabelingPalette.Brush(SampleLabelingPalette.LabelColors[2]),
                Bounds = new Rect(884, 470, 244, 168),
            },
            new SampleAnnotationRow
            {
                Id = "ann-4",
                Label = "划伤",
                Color = SampleLabelingPalette.Brush(SampleLabelingPalette.LabelColors[0]),
                Bounds = new Rect(348, 588, 286, 96),
            },
        ];

        SegmentationPolygons =
        [
            new SamplePolygonRow
            {
                Id = "poly-1",
                Label = "划伤",
                Color = SampleLabelingPalette.Brush(SampleLabelingPalette.LabelColors[0]),
                Points = [new Point(232, 196), new Point(508, 172), new Point(524, 268), new Point(248, 292)],
            },
        ];

        AutoFitCommand = new RelayCommand(() => { });
        ClearAnnotationsCommand = new RelayCommand(() => { });
        DeleteAnnotationCommand = new RelayCommand<string>(_ => { });
        DeletePolygonCommand = new RelayCommand<string>(_ => { });
        MarkReviewedBackgroundCommand = new RelayCommand(() => { });
        SelectAnnotationCommand = new RelayCommand<string>(_ => { });
        SetImageClassNgCommand = new RelayCommand(() => { });
        SetImageClassOkCommand = new RelayCommand(() => { });
        SetPolygonToolCommand = new RelayCommand(() => { });
        SetRectangleToolCommand = new RelayCommand(() => { });
        SetSelectToolCommand = new RelayCommand(() => { });
        UndoCommand = new RelayCommand(() => { });
        RedoCommand = new RelayCommand(() => { });
    }

    public IReadOnlyList<SampleAnnotationRow> AnnotationRows { get; }

    public IReadOnlyList<SamplePolygonRow> SegmentationPolygons { get; }

    public bool HasAnnotations => AnnotationRows.Count > 0;

    public bool HasSelectedAnnotations => true;

    public string SelectedAnnotationSummary => "划伤 · 318 × 132";

    public SampleAnnotationRow SelectedAnnotation => AnnotationRows[0];

    public int SelectedAnnotationCount => 1;

    public string ImageClassSummary => "OK";

    public bool IsImageClassOk => true;

    public bool IsImageClassNg => false;

    public bool IsReviewedBackground => false;

    public bool CanUseEditor => true;

    public bool CanUndo => true;

    public bool CanRedo => false;

    public bool CanClearAnnotations => true;

    public bool CanAutoFit => true;

    public LabelingToolMode ToolMode => LabelingToolMode.Rectangle;

    public bool IsSelectTool => false;

    public bool IsRectangleTool => true;

    public bool IsPolygonTool => false;

    public bool OcrConfirmed
    {
        get => _ocrConfirmed;
        set => SetProperty(ref _ocrConfirmed, value);
    }

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

    public ICommand AutoFitCommand { get; }

    public ICommand ClearAnnotationsCommand { get; }

    public ICommand DeleteAnnotationCommand { get; }

    public ICommand DeletePolygonCommand { get; }

    public ICommand MarkReviewedBackgroundCommand { get; }

    public ICommand SelectAnnotationCommand { get; }

    public ICommand SetImageClassNgCommand { get; }

    public ICommand SetImageClassOkCommand { get; }

    public ICommand SetPolygonToolCommand { get; }

    public ICommand SetRectangleToolCommand { get; }

    public ICommand SetSelectToolCommand { get; }

    public ICommand UndoCommand { get; }

    public ICommand RedoCommand { get; }
}

/// <summary>画布上的一行标注（对应 AnnotationRow）：几何用图像坐标，画布自己换算到屏幕。</summary>
public sealed class SampleAnnotationRow : ObservableObject
{
    private bool _isSelected;
    private bool _ignoreForTraining;
    private string _transcription = string.Empty;

    public required string Id { get; init; }

    public required string Label { get; init; }

    public required IBrush Color { get; init; }

    public required Rect Bounds { get; init; }

    public double Width => Bounds.Width;

    public double Height => Bounds.Height;

    public string Transcription
    {
        get => _transcription;
        set => SetProperty(ref _transcription, value);
    }

    public bool IgnoreForTraining
    {
        get => _ignoreForTraining;
        set => SetProperty(ref _ignoreForTraining, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

/// <summary>画布上的一个多边形（对应 SegmentationPolygonRow）。</summary>
public sealed class SamplePolygonRow
{
    public required string Id { get; init; }

    public required string Label { get; init; }

    public required IBrush Color { get; init; }

    public required IReadOnlyList<Point> Points { get; init; }

    public int PointCount => Points.Count;
}

/// <summary>类别列表 stub（对应 LabelingLabelsViewModel）。</summary>
public sealed class SampleLabelingLabels
{
    public SampleLabelingLabels()
    {
        LabelOptions =
        [
            new SampleLabelOption
            {
                Id = "label-1",
                Name = "划伤",
                HotkeyText = "1",
                Color = SampleLabelingPalette.Brush(SampleLabelingPalette.LabelColors[0]),
            },
            new SampleLabelOption
            {
                Id = "label-2",
                Name = "凹坑",
                HotkeyText = "2",
                Color = SampleLabelingPalette.Brush(SampleLabelingPalette.LabelColors[1]),
            },
            new SampleLabelOption
            {
                Id = "label-3",
                Name = "锈斑",
                HotkeyText = "3",
                Color = SampleLabelingPalette.Brush(SampleLabelingPalette.LabelColors[2]),
            },
            new SampleLabelOption
            {
                Id = "label-4",
                Name = "压痕",
                HotkeyText = "4",
                Color = SampleLabelingPalette.Brush(SampleLabelingPalette.LabelColors[3]),
            },
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

    public IBrush ActiveLabelColor { get; }

    public SampleLabelOption? SelectedLabel { get; }

    public bool CanManageLabels => true;

    public ICommand DeleteLabelCommand { get; }

    public ICommand SelectLabelCommand { get; }

    public ICommand ShowAddLabelDialogCommand { get; }

    public ICommand ShowEditLabelColorDialogCommand { get; }
}

/// <summary>一个类别（对应 LabelOption）。</summary>
public sealed class SampleLabelOption
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string HotkeyText { get; init; }

    public required IBrush Color { get; init; }
}

/// <summary>样本条 stub（对应 LabelingSamplesViewModel）：胶片条上的 5 张缩略图从这里出。</summary>
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
                AiBadgeTone = "ok",
                HasAiBadge = true,
                AiBadgeText = "OK",
                AiBadgeDiffers = false,
                AiBadgeToolTip = "AI 判定：OK（置信度 0.98）",
            },
            new SampleLabelingSampleRow
            {
                Id = "sample-2",
                Index = 2,
                FileName = "cold-roll_004316.jpg",
                Thumbnail = SampleLabelingImages.CreateThumbnail(
                    SampleLabelingPalette.ThumbFrom, SampleLabelingPalette.ThumbTo),
                IsAnnotated = true,
                AiBadgeTone = "ng",
                HasAiBadge = true,
                AiBadgeText = "NG",
                AiBadgeDiffers = true,
                AiBadgeToolTip = "AI 判定：NG（置信度 0.87），与人工标注不一致",
            },
            new SampleLabelingSampleRow
            {
                Id = "sample-3",
                Index = 3,
                FileName = "cold-roll_004317.jpg",
                Thumbnail = SampleLabelingImages.CreateThumbnail(
                    SampleLabelingPalette.ThumbFrom, SampleLabelingPalette.ThumbTo),
                IsAnnotated = true,
                AiBadgeTone = "warn",
                HasAiBadge = true,
                AiBadgeText = "?",
                AiBadgeToolTip = "AI 置信度偏低，建议人工复核",
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
        MoveCurrentToTestDefectCommand = new RelayCommand(() => { });
        MoveCurrentToTestGoodCommand = new RelayCommand(() => { });
        MoveCurrentToTrainGoodCommand = new RelayCommand(() => { });
        NavigateNextSampleCommand = new RelayCommand(() => { });
        NavigatePreviousSampleCommand = new RelayCommand(() => { });
        SetImageClassNgAndMoveCommand = new RelayCommand(() => { });
        ShowMoveSamplesDialogCommand = new RelayCommand(() => { });
        ShowNgMoveTargetDialogCommand = new RelayCommand(() => { });
        ToggleSamplePanelCommand = new RelayCommand(() => { });
    }

    public IReadOnlyList<SampleLabelingSampleRow> SamplePageItems { get; }

    public bool HasSamples => true;

    public bool HasNoSamples => false;

    public string ActiveSamplePositionText => "3 / 120";

    public string SamplePageText => "1 / 24";

    public string SamplePanelToggleTooltip => "收起样本列表";

    public bool CanImportImages => true;

    public bool CanMoveCurrentToAnomalySystemGallery => false;

    public bool CanMoveSelectedSamples => true;

    public bool CanSetImageClassNgAndMove => false;

    public bool HasNgMoveTarget => false;

    public string NgAndMoveButtonToolTip => "标为 NG 并移入 NG 集";

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

    public ICommand MoveCurrentToTestDefectCommand { get; }

    public ICommand MoveCurrentToTestGoodCommand { get; }

    public ICommand MoveCurrentToTrainGoodCommand { get; }

    public ICommand NavigateNextSampleCommand { get; }

    public ICommand NavigatePreviousSampleCommand { get; }

    public ICommand SetImageClassNgAndMoveCommand { get; }

    public ICommand ShowMoveSamplesDialogCommand { get; }

    public ICommand ShowNgMoveTargetDialogCommand { get; }

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

    public bool HasAiBadge { get; init; }

    public string AiBadgeTone { get; init; } = "ok";

    public string AiBadgeText { get; init; } = string.Empty;

    public string AiBadgeToolTip { get; init; } = string.Empty;

    public bool AiBadgeDiffers { get; init; }

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

/// <summary>AI 判定 stub（对应 LabelingAiJudgeViewModel）：变差分类轨道与右栏结果卡都从这里取值。</summary>
public sealed class SampleLabelingAiJudge
{
    public SampleLabelingAiJudge()
    {
        Evidence =
        [
            "被测区域与 OK 标准的纹理分布差异集中在右上角",
            "最接近的标准样本为 ok-standard_014.png，相似度 0.83",
            "判定阈值：相似度低于 0.90 判为 NG",
        ];

        ClosestStandardImage = SampleLabelingImages.CreateThumbnail(
            SampleLabelingPalette.ThumbFrom, SampleLabelingPalette.ThumbTo);

        AddCurrentAsNgStandardCommand = new RelayCommand(() => { });
        AddCurrentAsOkStandardCommand = new RelayCommand(() => { });
        AdoptCommand = new RelayCommand(() => { });
        CancelBatchCommand = new RelayCommand(() => { });
        CancelJudgeCommand = new RelayCommand(() => { });
        JudgeCommand = new RelayCommand(() => { });
        RejudgeCommand = new RelayCommand(() => { });
        ShowBatchDialogCommand = new RelayCommand(() => { });
        ShowModelSettingsDialogCommand = new RelayCommand(() => { });
        ShowReviewDialogCommand = new RelayCommand(() => { });
        ShowStandardsDialogCommand = new RelayCommand(() => { });
    }

    public SampleLabelingAiJudgeBatch Batch { get; } = new();

    public IReadOnlyList<string> Evidence { get; }

    public IImage? ClosestStandardImage { get; }

    public bool IsVisible => true;

    public bool IsJudging => false;

    public bool HasOutcome => true;

    public bool HasMessage => false;

    public bool HasEvidence => true;

    public bool HasStaleReason => false;

    public bool HasClosestStandard => true;

    public bool HasConfidence => true;

    public bool HasAdoptedText => true;

    public bool CanAdopt => true;

    public bool HasBatchSummaryHere => true;

    public bool HasReadinessHint => false;

    public bool IsBatchRunningElsewhere => false;

    public string VerdictText => "NG";

    public string ResultTone => "ng";

    public string ConfidenceText => "0.87";

    public string ClosestStandardText => "ok-standard_014.png · 相似度 0.83";

    public string ElapsedText => "耗时 1.2 s";

    public string ResultMetaText => "模型 ai-judge-swin-v3 · 1280 × 860";

    public string SuggestionSummary => "建议人工复核：差异区域靠近边缘，可能受光照影响";

    public string StaleReason => string.Empty;

    public string AdoptedText => "已采纳为 NG";

    public string ModelSummary => "ai-judge-swin-v3（NG 标准 42 张 / OK 标准 96 张）";

    public string StandardSummary => "OK 标准 96 张 · NG 标准 42 张";

    public string ReadinessHint => string.Empty;

    public string RemainingText => "剩余 0 张";

    public string SummaryText => "本页已判定 24 张：OK 19 / NG 5";

    public string UsageText => "本次会话已判定 128 张";

    public string EndpointTag => "local";

    public ICommand AddCurrentAsNgStandardCommand { get; }

    public ICommand AddCurrentAsOkStandardCommand { get; }

    public ICommand AdoptCommand { get; }

    public ICommand CancelBatchCommand { get; }

    public ICommand CancelJudgeCommand { get; }

    public ICommand JudgeCommand { get; }

    public ICommand RejudgeCommand { get; }

    public ICommand ShowBatchDialogCommand { get; }

    public ICommand ShowModelSettingsDialogCommand { get; }

    public ICommand ShowReviewDialogCommand { get; }

    public ICommand ShowStandardsDialogCommand { get; }
}

/// <summary>AI 判定批处理 stub（对应 LabelingAiJudgeBatchViewModel）。</summary>
public sealed class SampleLabelingAiJudgeBatch
{
    public bool IsRunning => false;

    public string LastRunSummary => "上次批处理：24 张 / 用时 38 s";

    public double ProgressPercent => 62;

    public string ProgressText => "15 / 24";

    public string StatisticsText => "OK 19 · NG 5 · 待复核 3";
}

/// <summary>模板匹配 stub（对应 LabelingTemplateMatchViewModel）。</summary>
public sealed class SampleTemplateMatch
{
    public SampleTemplateMatch()
    {
        SetTemplateCommand = new RelayCommand(() => { });
        ShowTemplateMatchDialogCommand = new RelayCommand(() => { });
        TemplateMatchCommand = new RelayCommand(() => { });
    }

    public bool IsVisible => true;

    public bool CanSetTemplate => true;

    public bool CanTemplateMatch => true;

    public ICommand SetTemplateCommand { get; }

    public ICommand ShowTemplateMatchDialogCommand { get; }

    public ICommand TemplateMatchCommand { get; }
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
    public static readonly Color[] LabelColors =
    [
        Color.FromRgb(0xF5, 0x9E, 0x0B),
        Color.FromRgb(0x3B, 0x82, 0xF6),
        Color.FromRgb(0xEF, 0x44, 0x44),
        Color.FromRgb(0x10, 0xB9, 0x81),
    ];

    public static readonly Color ThumbFrom = Color.FromRgb(0x33, 0x41, 0x55);

    public static readonly Color ThumbTo = Color.FromRgb(0x8B, 0x9C, 0xB0);

    public static IBrush Brush(Color color) => new SolidColorBrush(color);
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
