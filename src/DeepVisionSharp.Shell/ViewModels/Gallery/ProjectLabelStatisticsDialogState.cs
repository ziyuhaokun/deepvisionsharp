using CommunityToolkit.Mvvm.ComponentModel;
using DeepVisionSharp.Application.Ports;

namespace DeepVisionSharp.Shell.ViewModels.Gallery;

public sealed partial class ProjectLabelStatisticsDialogState : ObservableObject
{
    private static readonly IReadOnlyList<ProjectLabelTopCountOption> SharedTopCountOptions =
    [
        new(10, "Top 10"),
        new(15, "Top 15"),
        new(20, "Top 20"),
        new(30, "Top 30"),
        new(0, "全部"),
    ];

    private static readonly IReadOnlyList<ProjectLabelChartTypeOption> ProjectChartTypeOptions =
    [
        new(ProjectLabelChartType.HorizontalBar, "横向条形图"),
        new(ProjectLabelChartType.StackedByGallery, "按图库堆叠"),
        new(ProjectLabelChartType.Pie, "饼图"),
        new(ProjectLabelChartType.Donut, "环形图"),
    ];

    private static readonly IReadOnlyList<ProjectLabelChartTypeOption> GalleryChartTypeOptions =
    [
        new(ProjectLabelChartType.HorizontalBar, "横向条形图"),
        new(ProjectLabelChartType.Pie, "饼图"),
        new(ProjectLabelChartType.Donut, "环形图"),
    ];

    public IReadOnlyList<ProjectLabelTopCountOption> TopCountOptions => SharedTopCountOptions;

    [ObservableProperty]
    private IReadOnlyList<ProjectLabelChartTypeOption> _chartTypeOptions = ProjectChartTypeOptions;

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private string _scopeDescription = "统计范围：该项目下所有图库的所有标签（按样本计数）。";

    [ObservableProperty]
    private int _totalLabels;

    [ObservableProperty]
    private int _totalSamplesWithLabels;

    [ObservableProperty]
    private int _maxSampleCount;

    [ObservableProperty]
    private int _selectedTopCount = 15;

    [ObservableProperty]
    private ProjectLabelChartType _selectedChartType = ProjectLabelChartType.HorizontalBar;

    [ObservableProperty]
    private IReadOnlyList<ProjectLabelCountRow> _rows = [];

    [ObservableProperty]
    private IReadOnlyList<ProjectGalleryLabelCountRow> _galleryRows = [];

    /// <summary>Project-wide statistics.</summary>
    public void Reset(string projectName, ProjectLabelStatisticsSnapshot snapshot)
        => ResetCore(
            projectName,
            "统计范围：该项目下所有图库的所有标签（按样本计数）。",
            snapshot,
            isGalleryScope: false);

    /// <summary>Single-gallery statistics.</summary>
    public void ResetForGallery(string galleryName, ProjectLabelStatisticsSnapshot snapshot)
        => ResetCore(
            galleryName,
            "统计范围：该图库内所有标签（按样本计数）。",
            snapshot,
            isGalleryScope: true);

    private void ResetCore(
        string displayName,
        string scopeDescription,
        ProjectLabelStatisticsSnapshot snapshot,
        bool isGalleryScope)
    {
        DisplayName = displayName;
        ScopeDescription = scopeDescription;
        ChartTypeOptions = isGalleryScope ? GalleryChartTypeOptions : ProjectChartTypeOptions;
        Rows = snapshot.LabelCounts;
        GalleryRows = snapshot.GalleryLabelCounts;
        SelectedTopCount = 15;
        SelectedChartType = ProjectLabelChartType.HorizontalBar;
        TotalLabels = snapshot.LabelCounts.Count;
        TotalSamplesWithLabels = snapshot.LabelCounts.Sum(item => item.SampleCount);
        MaxSampleCount = snapshot.LabelCounts.Count > 0
            ? snapshot.LabelCounts.Max(item => item.SampleCount)
            : 0;
    }
}

public sealed record ProjectLabelTopCountOption(int Value, string Label);
