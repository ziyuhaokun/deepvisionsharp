using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DeepVisionSharp.Shell.Avalonia.ViewModels;

/// <summary>
/// 模型页 stub 上下文：绑定路径与 deepvision 的 ModelsViewModel 同名同层
/// （List / Editor / LoadCommand），值全部是假数据，命令点了不做事。
/// </summary>
public sealed class SampleModelsContext : ObservableObject
{
    public SampleModelsContext()
    {
        LoadCommand = new RelayCommand(() => { });
    }

    public SampleModelsList List { get; } = new();

    public SampleModelsEditor Editor { get; } = new();

    public ICommand LoadCommand { get; }
}

/// <summary>模型列表 stub（对应 ModelsListViewModel）。</summary>
public sealed class SampleModelsList : ObservableObject
{
    private string _keyword = string.Empty;
    private string _selectedStatus = string.Empty;

    public SampleModelsList()
    {
        FilteredModels =
        [
            new SampleModel
            {
                Id = "sample-model-1",
                Name = "钢板缺陷检测 yolo11n",
                Description = "冷轧带钢表面划伤、凹坑与锈斑检测，6 类框标注训练。",
                Status = "训练完成",
                ModelTypeDisplay = "YOLO 检测",
                ClassCountText = "6 类",
                ShowsInputChannelsBadge = true,
                InputChannelsDisplay = "RGB 三通道",
                ShowsOpenVinoBadge = true,
                OpenVinoBadgeText = "OpenVINO IR",
                ShowsSpatialBadge = true,
                SpatialBadgeText = "pad32 640×640",
                MetricsText = "mAP50 0.912 · mAP 0.687",
                SummaryText = "RGB 三通道 · 整图 pad32 640×640 · 推理上限 1280 · 120 epochs",
                CheckpointPath = "models/yolo11n-steel/best.pt",
                CreatedText = "2025-11-04 09:12",
                CanConvertOpenVino = true,
                CanQuantizeOpenVinoInt8 = true,
                CanExportOpenVino = true,
            },
            new SampleModel
            {
                Id = "sample-model-2",
                Name = "电芯外观分类 yolo11s-cls",
                Description = "整张图像判定 OK/NG，图像级标注，用于产线快速分选。",
                Status = "已发布",
                ModelTypeDisplay = "YOLO 分类",
                ClassCountText = "2 类",
                ShowsInputChannelsBadge = true,
                InputChannelsDisplay = "单通道灰度",
                ShowsOpenVinoBadge = true,
                OpenVinoBadgeText = "OpenVINO IR INT8",
                ShowsSpatialBadge = false,
                SpatialBadgeText = string.Empty,
                MetricsText = "mAP50 0.978",
                SummaryText = "单通道灰度 · 224 epochs",
                CheckpointPath = "models/yolo11s-cell/best.pt",
                CreatedText = "2025-10-28 15:40",
                CanConvertOpenVino = true,
                CanQuantizeOpenVinoInt8 = true,
                CanExportOpenVino = true,
            },
            new SampleModel
            {
                Id = "sample-model-3",
                Name = "注塑件异常 patchcore",
                Description = "无监督学习良品分布检测异常，无需逐样本标注。",
                Status = "已发布",
                ModelTypeDisplay = "异常检测 (Anomalib)",
                ClassCountText = "0 类",
                ShowsInputChannelsBadge = true,
                InputChannelsDisplay = "RGB 三通道",
                ShowsOpenVinoBadge = true,
                OpenVinoBadgeText = "OpenVINO IR",
                ShowsSpatialBadge = false,
                SpatialBadgeText = string.Empty,
                MetricsText = "暂无训练指标",
                SummaryText = "RGB 三通道 · 尚未读取到训练元数据",
                CheckpointPath = "models/patchcore-injection/weights.ckpt",
                CreatedText = "2025-10-19 11:03",
                CanConvertOpenVino = true,
                CanQuantizeOpenVinoInt8 = false,
                CanExportOpenVino = true,
            },
            new SampleModel
            {
                Id = "sample-model-4",
                Name = "瓶盖字符检测 PP-OCR",
                Description = "检测文字区域，供 PP-OCR 识别串联使用。",
                Status = "训练完成",
                ModelTypeDisplay = "PP-OCR 检测",
                ClassCountText = "1 类",
                ShowsInputChannelsBadge = false,
                InputChannelsDisplay = string.Empty,
                ShowsOpenVinoBadge = false,
                OpenVinoBadgeText = string.Empty,
                ShowsSpatialBadge = false,
                SpatialBadgeText = string.Empty,
                MetricsText = "Hmean 94.1%",
                SummaryText = "尚未读取到训练元数据",
                CheckpointPath = "models/ppocr-det-cap/best.pdparams",
                CreatedText = "2025-11-02 20:26",
                CanExportPpOcrDetection = true,
            },
            new SampleModel
            {
                Id = "sample-model-5",
                Name = "焊缝分割 yolo11n-seg",
                Description = "焊缝区域实例分割，尚未开始训练，只有初始化配置。",
                Status = "待训练",
                ModelTypeDisplay = "YOLO 分割",
                ClassCountText = "3 类",
                ShowsInputChannelsBadge = true,
                InputChannelsDisplay = "RGB 三通道",
                ShowsOpenVinoBadge = false,
                OpenVinoBadgeText = string.Empty,
                ShowsSpatialBadge = true,
                SpatialBadgeText = "静态画布 1024×1024",
                MetricsText = "暂无训练指标",
                SummaryText = "RGB 三通道 · 静态画布 1024×1024 · 训练窗 768×768 · 0 epochs",
                CheckpointPath = "models/yolo11n-weld",
                CreatedText = "2025-11-06 08:55",
            },
        ];

        StatusOptions =
        [
            new ModelStatusOption(string.Empty, "全部状态"),
            new ModelStatusOption("创建中", "创建中"),
            new ModelStatusOption("待训练", "待训练"),
            new ModelStatusOption("训练中", "训练中"),
            new ModelStatusOption("训练完成", "训练完成"),
            new ModelStatusOption("已发布", "已发布"),
            new ModelStatusOption("归档", "归档"),
        ];

        ClearFiltersCommand = new RelayCommand(() => { });
        OpenModelFolderCommand = new RelayCommand(() => { });
        ExportModelCommand = new RelayCommand(() => { });
        RetryPpOcrDetectionExportCommand = new RelayCommand(() => { });
        ExportPpOcrDetectionBundleCommand = new RelayCommand(() => { });
        ConvertOpenVinoCommand = new RelayCommand(() => { });
        QuantizeOpenVinoInt8Command = new RelayCommand(() => { });
        ExportOpenVinoCommand = new RelayCommand(() => { });
        ConfirmDeleteModelCommand = new RelayCommand(() => { });
    }

    public string OverviewSummary => "5 个模型 · 4 个已就绪 · 1 个待训练";

    public string Keyword
    {
        get => _keyword;
        set => SetProperty(ref _keyword, value);
    }

    public string SelectedStatus
    {
        get => _selectedStatus;
        set => SetProperty(ref _selectedStatus, value);
    }

    public IReadOnlyList<ModelStatusOption> StatusOptions { get; }

    public IReadOnlyList<SampleModel> FilteredModels { get; }

    public bool CanCreateModels => true;

    public bool HasModels => true;

    public bool HasFilteredModels => true;

    public bool HasEmptyState => !HasFilteredModels;

    public string EmptyStateTitle => "未找到匹配模型";

    public string EmptyStateDescription => "尝试调整搜索关键词或状态筛选";

    public bool IsConvertingOpenVino => false;

    public int OpenVinoConversionProgress => 0;

    public string OpenVinoConversionProgressText => $"{OpenVinoConversionProgress}%";

    public ICommand ClearFiltersCommand { get; }

    public ICommand OpenModelFolderCommand { get; }

    public ICommand ExportModelCommand { get; }

    public ICommand RetryPpOcrDetectionExportCommand { get; }

    public ICommand ExportPpOcrDetectionBundleCommand { get; }

    public ICommand ConvertOpenVinoCommand { get; }

    public ICommand QuantizeOpenVinoInt8Command { get; }

    public ICommand ExportOpenVinoCommand { get; }

    public ICommand ConfirmDeleteModelCommand { get; }
}

/// <summary>模型编辑 stub（对应 ModelsEditorViewModel）。</summary>
public sealed class SampleModelsEditor
{
    public SampleModelsEditor()
    {
        ShowCreateModelDialogCommand = new RelayCommand(() => { });
        ShowEditModelDialogCommand = new RelayCommand(() => { });
    }

    public bool ShowEmptyCreateAction => true;

    public ICommand ShowCreateModelDialogCommand { get; }

    public ICommand ShowEditModelDialogCommand { get; }
}

/// <summary>状态筛选项（对应 ModelStatusOption）。</summary>
public sealed record ModelStatusOption(string Value, string Label);

/// <summary>模型卡片 stub（对应 ModelRow）：只保留页面绑定用到的展示字段。</summary>
public sealed class SampleModel
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string Description { get; init; }

    public required string Status { get; init; }

    public required string ModelTypeDisplay { get; init; }

    public required string ClassCountText { get; init; }

    public required bool ShowsInputChannelsBadge { get; init; }

    public required string InputChannelsDisplay { get; init; }

    public required bool ShowsOpenVinoBadge { get; init; }

    public required string OpenVinoBadgeText { get; init; }

    public required bool ShowsSpatialBadge { get; init; }

    public required string SpatialBadgeText { get; init; }

    public required string MetricsText { get; init; }

    public required string SummaryText { get; init; }

    public required string CheckpointPath { get; init; }

    public required string CreatedText { get; init; }

    public bool CanExportPpOcrDetection { get; init; }

    public bool CanConvertOpenVino { get; init; }

    public bool CanQuantizeOpenVinoInt8 { get; init; }

    public bool CanExportOpenVino { get; init; }
}
