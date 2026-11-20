using System.Windows.Input;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DeepVisionSharp.Shell.Avalonia.ViewModels;

/// <summary>
/// 首页 stub 上下文：绑定路径与 deepvision 的 DashboardViewModel 同名同层
/// （List / Editor / Transfer / CardColumns），值全部是假数据，命令点了不做事。
/// </summary>
public sealed class SampleDashboardContext : ObservableObject
{
    private int _cardColumns = 3;

    public SampleDashboardList List { get; } = new();

    public SampleDashboardEditor Editor { get; } = new();

    public SampleDashboardTransfer Transfer { get; } = new();

    /// <summary>卡片栏数：由页面按宽度算出来回写（deepvision 里在 DashboardViewModel 上）。</summary>
    public int CardColumns
    {
        get => _cardColumns;
        set => SetProperty(ref _cardColumns, value);
    }
}

/// <summary>项目列表 stub（对应 DashboardProjectListViewModel）。</summary>
public sealed class SampleDashboardList : ObservableObject
{
    private string _keyword = string.Empty;
    private string _selectedType = string.Empty;

    public SampleDashboardList()
    {
        FilteredProjects =
        [
            new SampleProject
            {
                Id = "sample-project-1",
                Name = "钢板表面缺陷检测",
                Description = "检测冷轧带钢表面的划伤、凹坑与锈斑，按框标注后训练检测模型。",
                TypeKey = "ObjectDetection",
                TypeLabel = "目标检测",
                SampleCountText = "1,286 样本",
                LabelCountText = "3,842 标签",
                LastUpdatedText = "12 分钟前",
            },
            new SampleProject
            {
                Id = "sample-project-2",
                Name = "电芯外观分类",
                Description = "对整张图像判定 OK/NG，仅图像级标注，用于产线快速分选。",
                TypeKey = "Classification",
                TypeLabel = "图像分类",
                SampleCountText = "2,048 样本",
                LabelCountText = "2,048 标签",
                LastUpdatedText = "3 小时前",
            },
            new SampleProject
            {
                Id = "sample-project-3",
                Name = "注塑件异常检测",
                Description = "无监督学习良品分布检测异常，无需逐样本标注。",
                TypeKey = "UnsupervisedAnomaly",
                TypeLabel = "异常检测",
                SampleCountText = "980 样本",
                LabelCountText = "980 标签",
                LastUpdatedText = "2 天前",
            },
            new SampleProject
            {
                Id = "sample-project-4",
                Name = "瓶盖字符识别",
                Description = "标注文字区域和对应文本，用于训练工业字符识别模型。",
                TypeKey = "TextRecognition",
                TypeLabel = "字符识别",
                SampleCountText = "1,140 样本",
                LabelCountText = "4,096 标签",
                LastUpdatedText = "5 天前",
            },
        ];

        ProjectTypeOptions =
        [
            new ProjectTypeFilterOption(string.Empty, "全部类型"),
            new ProjectTypeFilterOption("ObjectDetection", "目标检测"),
            new ProjectTypeFilterOption("Classification", "图像分类"),
            new ProjectTypeFilterOption("InstanceSegmentation", "实例分割"),
            new ProjectTypeFilterOption("SemanticSegmentation", "语义分割"),
            new ProjectTypeFilterOption("VariationClassification", "差异分类"),
            new ProjectTypeFilterOption("UnsupervisedAnomaly", "异常检测"),
            new ProjectTypeFilterOption("TextRecognition", "字符识别"),
        ];

        ClearFiltersCommand = new RelayCommand(() => { });
        EnterProjectCommand = new RelayCommand(() => { });
        ConfirmDeleteProjectCommand = new RelayCommand(() => { });
    }

    public string OverviewSummary => "4 个项目 · 5,454 个样本 · 10,966 个标签";

    public string Keyword
    {
        get => _keyword;
        set => SetProperty(ref _keyword, value);
    }

    public string SelectedType
    {
        get => _selectedType;
        set => SetProperty(ref _selectedType, value);
    }

    public IReadOnlyList<ProjectTypeFilterOption> ProjectTypeOptions { get; }

    public IReadOnlyList<SampleProject> FilteredProjects { get; }

    public bool HasProjects => true;

    public bool HasFilteredProjects => true;

    public bool HasEmptyState => false;

    public string EmptyStateTitle => "未找到匹配项目";

    public string EmptyStateDescription => "尝试调整搜索关键词或筛选条件";

    public bool IsLoading => false;

    public bool IsProjectOperationInProgress => false;

    public bool IsDeletingProject => false;

    public string DeleteTitleText => "正在删除项目";

    public string DeleteStageText => "清理样本与标注…";

    public string DeleteProgressPercentText => "0%";

    public bool IsDeleteProgressIndeterminate => true;

    public double DeleteProgressValue => 0;

    public string DeleteProgressText => "0 / 0 个样本";

    public ICommand ClearFiltersCommand { get; }

    public ICommand EnterProjectCommand { get; }

    public ICommand ConfirmDeleteProjectCommand { get; }
}

/// <summary>项目编辑 stub（对应 DashboardProjectEditorViewModel）。</summary>
public sealed class SampleDashboardEditor
{
    public SampleDashboardEditor()
    {
        ShowCreateProjectDialogCommand = new RelayCommand(() => { });
        ShowEditProjectDialogCommand = new RelayCommand(() => { });
    }

    public bool IsSavingProject => false;

    public ICommand ShowCreateProjectDialogCommand { get; }

    public ICommand ShowEditProjectDialogCommand { get; }
}

/// <summary>导入 / 导出 stub（对应 DashboardProjectTransferViewModel）。</summary>
public sealed class SampleDashboardTransfer
{
    public SampleDashboardTransfer()
    {
        ImportProjectCommand = new RelayCommand(() => { });
        ImportOptProjectCommand = new RelayCommand(() => { });
        ExportProjectCommand = new RelayCommand(() => { });
    }

    public bool IsTransferring => false;

    public bool IsNotTransferring => !IsTransferring;

    public string TransferStage => "正在导入项目";

    public string TransferCurrentFileDisplay => string.Empty;

    public string TransferProgressPercentText => "0%";

    public double TransferProgressValue => 0;

    public string TransferProgressText => "0 / 0 个文件";

    public ICommand ImportProjectCommand { get; }

    public ICommand ImportOptProjectCommand { get; }

    public ICommand ExportProjectCommand { get; }
}

/// <summary>项目类型筛选项（对应 ProjectTypeFilterOption）。</summary>
public sealed record ProjectTypeFilterOption(string Value, string Label);

/// <summary>项目卡片 stub（对应 ProjectRow）：本波没有缩略图，全部走类型图标。</summary>
public sealed class SampleProject
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string Description { get; init; }

    public required string TypeKey { get; init; }

    public required string TypeLabel { get; init; }

    public required string SampleCountText { get; init; }

    public required string LabelCountText { get; init; }

    public required string LastUpdatedText { get; init; }

    public bool HasThumbnail => Thumbnail is not null;

    public IImage? Thumbnail => null;
}
