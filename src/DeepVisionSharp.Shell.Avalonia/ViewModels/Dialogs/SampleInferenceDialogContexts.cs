using System.Windows.Input;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DeepVisionSharp.Shell.Avalonia.ViewModels.Dialogs;

/// <summary>
/// 自动推理对话框 stub：检测 / 实例分割 / 语义分割 / 分类 / 差异分类五种内容共用一个上下文，
/// 绑定路径与 deepvision 的各自 ViewModel 同名，只保留渲染需要的默认值。
/// </summary>
public class SampleAutoInferenceDialogContext : ObservableObject
{
    private double _inferenceConfidence = 0.35;
    private double _inferenceMaxDetections = 300;
    private double _inferenceBatchSize = 8;
    private bool _showInferenceConfigWarning;

    public SampleAutoInferenceDialogContext()
    {
        InferenceModels =
        [
            new SampleDisplayTextItem("yolov8s-defect 2025-11-12 · 目标检测"),
            new SampleDisplayTextItem("yolov8m-defect 2025-10-30 · 目标检测"),
        ];

        SelectedInferenceModel = InferenceModels[0];

        InferenceDeviceOptions =
        [
            new SampleDisplayNameItem("自动（GPU 优先）"),
            new SampleDisplayNameItem("CPU"),
            new SampleDisplayNameItem("GPU.0"),
        ];

        SelectedInferenceDevice = InferenceDeviceOptions[0];
    }

    public string InferenceDialogDescription =>
        "用已训练的模型批量生成标注：结果会写入当前图库的样本，已存在的同类标注不会被覆盖。";

    public IReadOnlyList<SampleDisplayTextItem> InferenceModels { get; }

    public SampleDisplayTextItem? SelectedInferenceModel { get; set; }

    public bool HasInferenceModels => true;

    public bool HasNoInferenceModels => false;

    public string InferenceNoModelsHint => "还没有可用的推理模型，请先在「模型」页导入或训练。";

    public bool ShowsInferenceDeviceSelector => true;

    public IReadOnlyList<SampleDisplayNameItem> InferenceDeviceOptions { get; }

    public SampleDisplayNameItem SelectedInferenceDevice { get; set; }

    public string OpenVinoDeviceDisplayText => "GPU.0（OpenVINO 固定设备）";

    public double InferenceConfidence
    {
        get => _inferenceConfidence;
        set => SetProperty(ref _inferenceConfidence, value);
    }

    public string ConfidenceThresholdLabel => "置信度阈值";

    public string ConfidenceThresholdHint => "低于阈值的候选框直接丢弃；检测类推荐 0.25–0.45。";

    public string InferencePolicySectionTitle => "后处理策略";

    public string InferenceSpatialPolicySummary =>
        "按项目空间策略裁剪越界框：超出图像边界的部分会被截断，完全在界外的框丢弃。";

    public double InferenceMaxDetections
    {
        get => _inferenceMaxDetections;
        set => SetProperty(ref _inferenceMaxDetections, value);
    }

    public double InferenceBatchSize
    {
        get => _inferenceBatchSize;
        set => SetProperty(ref _inferenceBatchSize, value);
    }

    public bool ShowMaxDetectionsParameter => true;

    public string InferenceParametersFooterHint =>
        "参数只影响本次批处理，不会写回模型配置。";

    public string InferenceFixedBatchHint => "该模型固定按单张推理，批次大小不可调。";

    public bool HasInferenceFixedBatchHint => false;

    public bool ShowInferenceConfigWarning
    {
        get => _showInferenceConfigWarning;
        set => SetProperty(ref _showInferenceConfigWarning, value);
    }

    public string InferenceDialogErrorMessage => string.Empty;

    public bool HasInferenceDialogErrorMessage => false;

    public bool ShowOcrParameters => false;

    public bool ShowOcrDetectionParameters => false;

    public IReadOnlyList<SampleValueLabelItem> OcrInferenceModeOptions { get; } =
    [
        new("TextDetectionAndRecognition", "检测 + 识别"),
        new("TextRecognitionOnly", "仅识别"),
    ];

    public SampleValueLabelItem? SelectedOcrInferenceMode { get; set; }

    public IReadOnlyList<SampleDisplayNameItem> OcrDetectorModels { get; } =
    [
        new("ch_PP-OCRv4_det"),
    ];

    public SampleDisplayNameItem? SelectedOcrDetectorModel { get; set; }

    public IReadOnlyList<SampleValueLabelItem> OcrDetectorDeviceOptions { get; } =
    [
        new("AUTO", "自动"),
        new("CPU", "CPU"),
    ];

    public SampleValueLabelItem? OcrDetectorDevice { get; set; }

    public double OcrDetectionThreshold { get; set; } = 0.3;
}

/// <summary>图库批量推理对话框 stub（三种视图：配置 / 运行中 / 完成）。</summary>
public sealed class SampleGalleryBatchInferenceDialogContext : ObservableObject
{
    private string _logText = string.Empty;
    private double _confidence = 0.35;
    private double _maxDetections = 300;
    private double _progressValue;

    public SampleGalleryBatchInferenceDialogContext()
    {
        Models =
        [
            new SampleInferenceModelItem("yolov8s-defect 2025-11-12 · 目标检测", "按项目空间策略裁剪越界框。"),
            new SampleInferenceModelItem("yolov8m-defect 2025-10-30 · 目标检测", "按项目空间策略裁剪越界框。"),
        ];

        SelectedModel = Models[0];

        DeviceOptions =
        [
            new SampleDisplayNameItem("自动（GPU 优先）"),
            new SampleDisplayNameItem("CPU"),
        ];

        SelectedDevice = DeviceOptions[0];

        ScopeOptions =
        [
            new SampleValueLabelItem("All", "全部样本"),
            new SampleValueLabelItem("Unannotated", "仅未标注样本"),
        ];

        SelectedScopeOption = ScopeOptions[0];

        WriteBackOptions =
        [
            new SampleValueLabelItem("Append", "追加到现有标注"),
            new SampleValueLabelItem("Replace", "替换同类标注"),
        ];

        SelectedWriteBackOption = WriteBackOptions[0];

        StartCommand = new RelayCommand(() => { });
        CancelRunCommand = new RelayCommand(() => { });
    }

    public string Subtitle => "对整库或筛选后的样本批量生成标注。任务在后台执行，可随时取消。";

    public string GalleryName => "钢板表面缺陷 · 一车间";

    public string GalleryMetaText => "1,286 张样本 · 3,842 个标签";

    public string ProjectTypeLabel => "目标检测";

    public bool IsConfigView => true;

    public bool IsRunningView => false;

    public bool IsDoneView => false;

    public bool IsConfigInteractive => true;

    public bool IsPreparing => false;

    public string PreparingStatusText => "正在准备图库推理…";

    public bool ShowOcrParameters => false;

    public IReadOnlyList<SampleInferenceModelItem> Models { get; }

    public SampleInferenceModelItem SelectedModel { get; set; }

    public bool HasNoModels => false;

    public string NoModelsHint => "还没有可用的推理模型。";

    public bool ShowsSelectedModelSpatialSummary => true;

    public IReadOnlyList<SampleValueLabelItem> ScopeOptions { get; }

    public SampleValueLabelItem SelectedScopeOption { get; set; }

    public string ScopeHint => "范围在开始推理时确定，运行中不可修改。";

    public IReadOnlyList<SampleDisplayNameItem> DeviceOptions { get; }

    public SampleDisplayNameItem SelectedDevice { get; set; }

    public double Confidence
    {
        get => _confidence;
        set => SetProperty(ref _confidence, value);
    }

    public bool ShowsMaxDetections => true;

    public double MaxDetections
    {
        get => _maxDetections;
        set => SetProperty(ref _maxDetections, value);
    }

    public IReadOnlyList<SampleValueLabelItem> WriteBackOptions { get; }

    public SampleValueLabelItem SelectedWriteBackOption { get; set; }

    public string WriteBackHint => "追加会保留已有标注；替换会先删掉同类标签的标注再写入。";

    public bool CanStart => true;

    public string StageText => "已完成";

    public string ProgressCountText => "0 / 0";

    public double ProgressValue
    {
        get => _progressValue;
        set => SetProperty(ref _progressValue, value);
    }

    public string CurrentFileText => string.Empty;

    public string LogText
    {
        get => _logText;
        set => SetProperty(ref _logText, value);
    }

    public string ResultTotal => "0";

    public string ResultWritten => "0";

    public string ResultSkipped => "0";

    public string ResultFailed => "0";

    public ICommand StartCommand { get; }

    public ICommand CancelRunCommand { get; }
}

/// <summary>推理图片来源对话框 stub。</summary>
public sealed class SampleInferenceImageSourceDialogContext
{
    public SampleInferenceImageSourceDialogContext()
    {
        Galleries =
        [
            new SampleDisplayTextItem("钢板表面缺陷 · 一车间（1,286 张）"),
            new SampleDisplayTextItem("瓶盖字符 · 二号线（1,140 张）"),
        ];

        SelectedGallery = Galleries[0];

        ImportExternalImagesCommand = new RelayCommand(() => { });
        ClearExternalImagesCommand = new RelayCommand(() => { });
        LoadGallerySamplesCommand = new RelayCommand(() => { });
        ClearGalleryImagesCommand = new RelayCommand(() => { });
        ClearAllImagesCommand = new RelayCommand(() => { });
    }

    public IReadOnlyList<SampleDisplayTextItem> Galleries { get; }

    public SampleDisplayTextItem SelectedGallery { get; set; }

    public string ImageSourceSummary => "外部图片 12 张 + 图库样本 240 张，共 252 张待推理。";

    public ICommand ImportExternalImagesCommand { get; }

    public ICommand ClearExternalImagesCommand { get; }

    public ICommand LoadGallerySamplesCommand { get; }

    public ICommand ClearGalleryImagesCommand { get; }

    public ICommand ClearAllImagesCommand { get; }
}

/// <summary>批量推理报告对话框 stub：图表位图由推理 lane 的渲染器负责，这里只给指标与样例。</summary>
public sealed class SampleInferenceBatchReportDialogContext
{
    public string SuccessRateText => "96.3%";

    public string SuccessCount => "1,238";

    public string TotalWallText => "4 分 12 秒";

    public string TotalCount => "1,286";

    public string AverageSuccessText => "187 ms";

    public string MinMaxSuccessText => "142 / 396 ms";

    public string FailedCount => "48";

    public string MissRateText => "2.1%";

    public string FalseAlarmRateText => "3.4%";

    public string AccuracyText => "94.5%";

    public string ConfusionText => "TP 412 / FP 14 / FN 9 / TN 405";

    public bool HasOriginalSamples => true;

    public IReadOnlyList<SampleInferenceSampleItem> OriginalSamples { get; } =
    [
        new("NG-000112.png", "预计 NG · 推理 NG"),
        new("NG-000187.png", "预计 NG · 推理 OK（漏检）"),
        new("OK-001204.png", "预计 OK · 推理 NG（误报）"),
        new("OK-000970.png", "预计 OK · 推理 OK"),
    ];
}

/// <summary>推理调试对话框 stub：只读报告文本。</summary>
public sealed class SampleInferenceDebugDialogContext
{
    public string InferenceDebugHeadline => "推理调试 · NG-000187.png";

    public string InferenceDebugReport =>
        """
        模型        yolov8s-defect 2025-11-12
        设备        GPU.0 (OpenVINO 2025.3.0)
        置信度      0.35
        引擎返回数  3
        可匹配标签  2
        ------------- 引擎原始输出 -------------
        box  (112, 84, 231, 176)  score 0.91  class 划伤 → 匹配「划伤」
        box  (340, 62, 402, 118)  score 0.72  class 凹坑 → 匹配「凹坑」
        box  (18, 210, 96, 279)   score 0.41  class 锈斑 → 项目标签缺失，丢弃
        """;
}

/// <summary>推理模型下拉项（DisplayText + 空间策略摘要）。</summary>
public sealed record SampleInferenceModelItem(string DisplayText, string SpatialPolicySummary);

/// <summary>报告里的原图样例项。</summary>
public sealed class SampleInferenceSampleItem
{
    public SampleInferenceSampleItem(string title, string verdict)
    {
        Title = title;
        Verdict = verdict;
    }

    public string Title { get; }

    public string Verdict { get; }

    /// <summary>stub 没有缩略图，Image 的 Source 保持空。</summary>
    public Bitmap? Thumbnail => null;
}
