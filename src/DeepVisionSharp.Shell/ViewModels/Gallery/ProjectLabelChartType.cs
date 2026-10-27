namespace DeepVisionSharp.Shell.ViewModels.Gallery;

public enum ProjectLabelChartType
{
    HorizontalBar,
    StackedByGallery,
    Pie,
    Donut,
}

public sealed record ProjectLabelChartTypeOption(ProjectLabelChartType Value, string Label);
