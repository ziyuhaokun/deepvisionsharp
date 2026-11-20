using Avalonia.Controls;

namespace DeepVisionSharp.Shell.Avalonia.Views.Dialogs.Contents;

/// <summary>
/// 标签统计内容。deepvision 版在这里按状态对象重绘图表位图（Rendering/ 的
/// ProjectLabelStatisticsChartRenderer）；图表渲染不属于本 lane，本波只保留布局与绑定位。
/// </summary>
public partial class ProjectLabelStatisticsDialogContent : UserControl
{
    public ProjectLabelStatisticsDialogContent() => InitializeComponent();
}
