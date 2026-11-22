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
    /// 与 WPF 页一致：已有数据时静默刷新（不再显示加载遮罩），首次进入才走完整加载。
    /// 异常与用户提示收敛在 ViewModel 的 <see cref="DashboardViewModel.LoadForNavigationAsync"/>。
    /// </summary>
    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        await _viewModel.LoadForNavigationAsync();
    }
}
