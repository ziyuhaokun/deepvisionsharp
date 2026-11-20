using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using DeepVisionSharp.Presentation.ViewModels.Training;
using DeepVisionSharp.Shell.Avalonia.ViewModels;

namespace DeepVisionSharp.Shell.Avalonia.Views;

/// <summary>
/// 训练页：左侧任务列表 + 右侧「设置 / 运行」双面板。
/// DataContext 来自容器的 <see cref="TrainingViewModel"/>；没有容器（设计时 / 无头渲染）时
/// 退回 <see cref="SampleTrainingContext"/>，绑定路径不变。
/// </summary>
public partial class TrainingPage : UserControl
{
    // 与 deepvision 的 PageViewportConstraints 同一口径：窗口高度 - 标题栏 - 页面上下留白。
    private const double TitleBarHeight = 44;
    private const double PageVerticalMargin = 56;

    private readonly TrainingViewModel? _viewModel;

    public TrainingPage()
        : this(null)
    {
    }

    public TrainingPage(TrainingViewModel? viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = (object?)viewModel ?? new SampleTrainingContext();

        SizeChanged += OnPageSizeChanged;
        AttachedToVisualTree += OnPageAttached;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private Control? Root => this.FindControl<Control>("PageRootDock");

    private void OnPageAttached(object? sender, VisualTreeAttachmentEventArgs e) => UpdateViewportConstraints();

    private void OnPageSizeChanged(object? sender, SizeChangedEventArgs e) => UpdateViewportConstraints();

    /// <summary>每次进入都重新拉任务列表；导航切走时先释放页面侧订阅（不停后端训练）。</summary>
    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        try
        {
            _viewModel.Activate();
            await _viewModel.JobList.LoadCommand.ExecuteAsync(null);
        }
        catch (OperationCanceledException)
        {
            // 导航走了／页面被取消，下一次进入会重新加载。
        }
        catch (Exception ex)
        {
            // async void：逃出去的异常会打断整个 UI 线程，这里就地记下。
            System.Diagnostics.Trace.WriteLine($"TrainingPage load failed: {ex}");
        }
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        try
        {
            _viewModel?.Deactivate();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"TrainingPage deactivate failed: {ex}");
        }
    }

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
