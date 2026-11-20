using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using DeepVisionSharp.Presentation.ViewModels;
using DeepVisionSharp.Presentation.ViewModels.Dashboard;
using DeepVisionSharp.Shell.Avalonia.ViewModels;

namespace DeepVisionSharp.Shell.Avalonia.Views;

/// <summary>
/// 首页（项目）页面。DataContext 来自容器的 <see cref="DashboardViewModel"/>；
/// 没有容器（设计时 / 无头渲染）时退回 <see cref="SampleDashboardContext"/>，绑定路径不变。
/// </summary>
public partial class DashboardPage : UserControl
{
    private readonly DashboardViewModel? _viewModel;
    private readonly ICardColumnsHost _cardColumns;

    public DashboardPage()
        : this(null)
    {
    }

    public DashboardPage(DashboardViewModel? viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        var context = (object?)viewModel ?? new SampleDashboardContext();
        DataContext = context;
        _cardColumns = (ICardColumnsHost)context;

        SizeChanged += OnPageSizeChanged;
        Loaded += OnLoaded;
    }

    /// <summary>与 deepvision 同一套分栏公式：可用宽度按 380 一分栏，1~4 栏。</summary>
    private void OnPageSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        var availableWidth = Math.Max(0, Bounds.Width - 64);
        _cardColumns.CardColumns = availableWidth > 0
            ? Math.Clamp((int)Math.Floor(availableWidth / 380), 1, 4)
            : 1;
    }

    /// <summary>
    /// 与 WPF 页一致：已有数据时静默刷新（不再显示加载遮罩），首次进入才走 <c>LoadCommand</c>。
    /// </summary>
    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        try
        {
            if (_viewModel.List.HasProjects)
            {
                await _viewModel.List.RefreshProjectsAsync();
            }
            else
            {
                await _viewModel.LoadCommand.ExecuteAsync(null);
            }
        }
        catch (OperationCanceledException)
        {
            // 导航走了，下一次进入会重新加载。
        }
        catch (Exception ex)
        {
            // async void：逃出去的异常会打断整个 UI 线程，这里就地记下。
            Trace.WriteLine($"DashboardPage load failed: {ex}");
        }
    }
}
