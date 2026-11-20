using Avalonia.Controls;

namespace DeepVisionSharp.Shell.Avalonia.Views.Dialogs.Contents;

/// <summary>
/// 批量推理报告内容。deepvision 版在这里按报告数据重绘四张图表位图（Rendering/ 的
/// InferenceBatchReportChartRenderer）；图表渲染属于推理 lane，本波只保留布局与绑定位。
/// </summary>
public partial class InferenceBatchReportDialogContent : UserControl
{
    public InferenceBatchReportDialogContent() => InitializeComponent();
}
