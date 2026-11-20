using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DeepVisionSharp.Shell.Avalonia.ViewModels.Dialogs;

/// <summary>图库编辑对话框 stub（名称 + 描述 + 校验错误）。</summary>
public sealed class SampleGalleryEditorDialogContext
{
    public string DraftName { get; set; } = "钢板表面缺陷 · 一车间";

    public string DraftDescription { get; set; } = "冷轧带钢表面缺陷样本，按框标注划伤、凹坑与锈斑。";

    public bool IsEditingSystemGallery => false;

    public string DialogErrorMessage => string.Empty;

    public bool HasDialogErrorMessage => false;
}

/// <summary>样本选择器对话框 stub（分页勾选）。</summary>
public sealed class SampleGallerySamplePickerDialogContext : ObservableObject
{
    private int _pageIndex;

    public SampleGallerySamplePickerDialogContext()
    {
        PageItems = Enumerable.Range(1, 8)
            .Select(index => new SamplePickerRow($"IMG_{index:D4}.png", index % 3 != 0))
            .ToArray();

        SelectAllOnPageCommand = new RelayCommand(() => SetPageSelection(true));
        ClearPageSelectionCommand = new RelayCommand(() => SetPageSelection(false));
        GoToPreviousPageCommand = new RelayCommand(() => PageIndex--);
        GoToNextPageCommand = new RelayCommand(() => PageIndex++);
    }

    public string GalleryName => "钢板表面缺陷 · 一车间";

    public string SelectionSummary => "已勾选 5 张样本，跨 2 页。";

    public IReadOnlyList<SamplePickerRow> PageItems { get; }

    public bool HasNoPageItems => false;

    public int PageIndex
    {
        get => _pageIndex;
        private set
        {
            if (SetProperty(ref _pageIndex, value))
            {
                OnPropertyChanged(nameof(PageText));
                OnPropertyChanged(nameof(CanGoToPreviousPage));
                OnPropertyChanged(nameof(CanGoToNextPage));
            }
        }
    }

    public string PageText => $"第 {PageIndex + 1} / 8 页";

    public bool CanGoToPreviousPage => PageIndex > 0;

    public bool CanGoToNextPage => PageIndex < 7;

    public string DialogErrorMessage => string.Empty;

    public bool HasDialogErrorMessage => false;

    public ICommand SelectAllOnPageCommand { get; }

    public ICommand ClearPageSelectionCommand { get; }

    public ICommand GoToPreviousPageCommand { get; }

    public ICommand GoToNextPageCommand { get; }

    private void SetPageSelection(bool selected)
    {
        foreach (var row in PageItems)
        {
            row.IsSelected = selected;
        }
    }
}

/// <summary>目标图库选择对话框 stub（新建 / 已有二选一）。</summary>
public sealed class SampleGalleryTargetSelectionDialogContext : ObservableObject
{
    private bool _useNewGallery = true;

    public SampleGalleryTargetSelectionDialogContext()
    {
        AvailableTargets =
        [
            new SampleGalleryTarget("gallery-2", "钢板表面缺陷 · 二车间（980 张）"),
            new SampleGalleryTarget("gallery-3", "钢板表面缺陷 · 复检（312 张）"),
        ];

        SelectedTargetGalleryId = AvailableTargets[0].Id;

        SelectNewTargetCommand = new RelayCommand(() => UseNewGallery = true);
        SelectExistingTargetCommand = new RelayCommand(() => UseNewGallery = false);
    }

    public string SummaryText => "把当前图库的 12 张样本合并到目标图库，样本的标注与图片一起移动。";

    public bool UseNewGallery
    {
        get => _useNewGallery;
        private set
        {
            if (SetProperty(ref _useNewGallery, value))
            {
                OnPropertyChanged(nameof(UseExistingGallery));
            }
        }
    }

    public bool UseExistingGallery => !_useNewGallery;

    public string NewGalleryName { get; set; } = "钢板表面缺陷 · 合并结果";

    public IReadOnlyList<SampleGalleryTarget> AvailableTargets { get; }

    public string SelectedTargetGalleryId { get; set; }

    public bool DeleteEmptySources { get; set; } = true;

    public bool ShowDeleteEmptySourcesOption => true;

    public string DialogErrorMessage => string.Empty;

    public bool HasDialogErrorMessage => false;

    public ICommand SelectNewTargetCommand { get; }

    public ICommand SelectExistingTargetCommand { get; }
}

/// <summary>图库拆分计划对话框 stub：左侧拆分方式列表 + 右侧参数与预计结果。</summary>
public sealed class SampleGallerySplitPlannerDialogContext : ObservableObject
{
    private string _mode = "Ratio";
    private bool _isRandomSampling = true;

    public SampleGallerySplitPlannerDialogContext()
    {
        SelectByRatioModeCommand = new RelayCommand(() => Mode = "Ratio");
        SelectByCountModeCommand = new RelayCommand(() => Mode = "Count");
        SelectManualModeCommand = new RelayCommand(() => Mode = "Manual");
        SelectAnnotatedModeCommand = new RelayCommand(() => Mode = "Annotated");
        SelectUnannotatedModeCommand = new RelayCommand(() => Mode = "Unannotated");
        SelectWithErrorModeCommand = new RelayCommand(() => Mode = "WithError");
        SelectFileNamePrefixModeCommand = new RelayCommand(() => Mode = "FileNamePrefix");
        SelectByLabelModeCommand = new RelayCommand(() => Mode = "Label");
        SelectByLabelRatioModeCommand = new RelayCommand(() => Mode = "LabelRatio");
        SelectRandomSamplingCommand = new RelayCommand(() => IsRandomSampling = true);
        SelectFileOrderSamplingCommand = new RelayCommand(() => IsRandomSampling = false);
    }

    public string GalleryName => "钢板表面缺陷 · 一车间";

    public int TotalSampleCount => 1286;

    public string StepText => "第 1 步 / 共 2 步";

    public string Mode
    {
        get => _mode;
        private set
        {
            if (SetProperty(ref _mode, value))
            {
                OnPropertyChanged(nameof(IsByRatioMode));
                OnPropertyChanged(nameof(IsByCountMode));
                OnPropertyChanged(nameof(IsManualMode));
                OnPropertyChanged(nameof(IsAnnotatedMode));
                OnPropertyChanged(nameof(IsUnannotatedMode));
                OnPropertyChanged(nameof(IsWithErrorMode));
                OnPropertyChanged(nameof(IsFileNamePrefixMode));
                OnPropertyChanged(nameof(IsByLabelMode));
                OnPropertyChanged(nameof(IsByLabelRatioMode));
                OnPropertyChanged(nameof(SelectedModeTitle));
                OnPropertyChanged(nameof(SelectedModeDescription));
                OnPropertyChanged(nameof(ShowRatioOptions));
                OnPropertyChanged(nameof(ShowCountOptions));
                OnPropertyChanged(nameof(ShowManualOptions));
                OnPropertyChanged(nameof(ShowFileNamePrefixOptions));
                OnPropertyChanged(nameof(ShowLabelOptions));
            }
        }
    }

    public bool IsByRatioMode => _mode == "Ratio";

    public bool IsByCountMode => _mode == "Count";

    public bool IsManualMode => _mode == "Manual";

    public bool IsAnnotatedMode => _mode == "Annotated";

    public bool IsUnannotatedMode => _mode == "Unannotated";

    public bool IsWithErrorMode => _mode == "WithError";

    public bool IsFileNamePrefixMode => _mode == "FileNamePrefix";

    public bool IsByLabelMode => _mode == "Label";

    public bool IsByLabelRatioMode => _mode == "LabelRatio";

    public string SelectedModeTitle => _mode switch
    {
        "Count" => "按数量拆分",
        "Manual" => "手动选择样本",
        "Annotated" => "移动全部已标注样本",
        "Unannotated" => "移动全部未标注样本",
        "WithError" => "移动全部错误样本",
        "FileNamePrefix" => "按文件名前缀拆分",
        "Label" => "按缺陷类别拆分",
        "LabelRatio" => "按类别比例拆分",
        _ => "按比例拆分",
    };

    public string SelectedModeDescription => _mode switch
    {
        "Count" => "从源图库抽取指定张数，源图库至少保留 1 张。",
        "Manual" => "下一步分页勾选需要移动的样本。",
        "Annotated" => "把所有已标注样本整体移动到目标图库。",
        "Unannotated" => "把所有未标注样本整体移动到目标图库。",
        "WithError" => "把标注存在错误的样本整体移动到目标图库。",
        "FileNamePrefix" => "按文件名前缀匹配需要移动的样本。",
        "Label" => "移动包含指定缺陷类别的全部样本。",
        "LabelRatio" => "按每个类别的比例分别抽取样本。",
        _ => "按百分比随机或按文件名顺序抽取样本。",
    };

    public bool ShowRatioOptions => _mode is "Ratio" or "LabelRatio";

    public bool ShowCountOptions => _mode == "Count";

    public bool ShowFileNamePrefixOptions => _mode == "FileNamePrefix";

    public bool ShowLabelOptions => _mode == "Label";

    public bool ShowManualOptions => _mode == "Manual";

    public double MoveRatioPercent { get; set; } = 20;

    public double MoveCount { get; set; } = 120;

    public double MaxMoveCount => 1285;

    public bool IsRandomSampling
    {
        get => _isRandomSampling;
        private set
        {
            if (SetProperty(ref _isRandomSampling, value))
            {
                OnPropertyChanged(nameof(PreserveFileOrder));
            }
        }
    }

    public bool PreserveFileOrder => !_isRandomSampling;

    public string RandomSeedText { get; set; } = string.Empty;

    public bool HasRandomSeedError => false;

    public string FileNamePrefixText { get; set; } = "batch_a_";

    public IReadOnlyList<string> AvailableLabels { get; } = ["划伤", "凹坑", "锈斑"];

    public string SelectedLabel { get; set; } = "划伤";

    public bool HasAvailableLabels => true;

    public bool IsPreviewLoading => false;

    public bool HasPreviewMetrics => true;

    public bool HasPreviewError => false;

    public string PreviewText => "按比例 20% 随机抽取，源图库保留 1,029 张。";

    public string PreviewErrorMessage => string.Empty;

    public int PreviewMoveSampleCount => 257;

    public int PreviewRemainingSampleCount => 1029;

    public string DialogErrorMessage => string.Empty;

    public bool HasDialogErrorMessage => false;

    public ICommand SelectByRatioModeCommand { get; }

    public ICommand SelectByCountModeCommand { get; }

    public ICommand SelectManualModeCommand { get; }

    public ICommand SelectAnnotatedModeCommand { get; }

    public ICommand SelectUnannotatedModeCommand { get; }

    public ICommand SelectWithErrorModeCommand { get; }

    public ICommand SelectFileNamePrefixModeCommand { get; }

    public ICommand SelectByLabelModeCommand { get; }

    public ICommand SelectByLabelRatioModeCommand { get; }

    public ICommand SelectRandomSamplingCommand { get; }

    public ICommand SelectFileOrderSamplingCommand { get; }
}

/// <summary>标签统计对话框 stub：图表位图由图表渲染器负责，这里只给指标与下拉项。</summary>
public sealed class SampleProjectLabelStatisticsDialogContext
{
    public string DisplayName => "钢板表面缺陷 · 一车间";

    public string ScopeDescription => "统计范围：当前图库全部 1,286 张样本的锚框标注。";

    public int TotalLabels => 3;

    public int TotalSamplesWithLabels => 1244;

    public int MaxSampleCount => 986;

    public IReadOnlyList<SampleValueLabelItem> ChartTypeOptions { get; } =
    [
        new("Bar", "柱状图"),
        new("Pie", "饼图"),
    ];

    public string SelectedChartType { get; set; } = "Bar";

    public IReadOnlyList<SampleValueLabelItem> TopCountOptions { get; } =
    [
        new("10", "前 10 项"),
        new("20", "前 20 项"),
        new("All", "全部"),
    ];

    public string SelectedTopCount { get; set; } = "10";
}

/// <summary>样本选择器的一行：勾选状态可写。</summary>
public sealed class SamplePickerRow : ObservableObject
{
    private bool _isSelected;

    public SamplePickerRow(string fileName, bool isSelected)
    {
        FileName = fileName;
        _isSelected = isSelected;
    }

    public string FileName { get; }

    public string AnnotatedText => _isSelected ? "已标注 3 个" : "未标注";

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
            {
                OnPropertyChanged(nameof(AnnotatedText));
            }
        }
    }
}

/// <summary>目标图库下拉项。</summary>
public sealed record SampleGalleryTarget(string Id, string DisplayText);
