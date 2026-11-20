using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using DeepVisionSharp.Shell.Avalonia.ViewModels;

namespace DeepVisionSharp.Shell.Avalonia.Views;

/// <summary>
/// 训练页：左侧任务列表 + 右侧「设置 / 运行」双面板。
/// 本波用 stub 数据（任务卡 3 张、指标曲线有数据、日志 20 行），不接业务 ViewModel，也没有轮询。
/// </summary>
public partial class TrainingPage : UserControl
{
    // 与 deepvision 的 PageViewportConstraints 同一口径：窗口高度 - 标题栏 - 页面上下留白。
    private const double TitleBarHeight = 44;
    private const double PageVerticalMargin = 56;

    public TrainingPage()
    {
        InitializeComponent();
        DataContext = new SampleTrainingContext();
        SizeChanged += OnPageSizeChanged;
        AttachedToVisualTree += OnPageAttached;
    }

    private Control? Root => this.FindControl<Control>("PageRootDock");

    private void OnPageAttached(object? sender, VisualTreeAttachmentEventArgs e) => UpdateViewportConstraints();

    private void OnPageSizeChanged(object? sender, SizeChangedEventArgs e) => UpdateViewportConstraints();

    /// <summary>把页面根节点高度限制在可用高度内，让页内 ScrollViewer 成为唯一滚动容器。</summary>
    private void UpdateViewportConstraints()
    {
        if (Root is not { } root || TopLevel.GetTopLevel(this) is not Window window)
        {
            return;
        }

        var windowHeight = window.ClientSize.Height;
        if (windowHeight <= 0)
        {
            return;
        }

        var pageHeight = windowHeight - TitleBarHeight - PageVerticalMargin;
        if (pageHeight > 0)
        {
            root.MaxHeight = pageHeight;
        }
    }
}
