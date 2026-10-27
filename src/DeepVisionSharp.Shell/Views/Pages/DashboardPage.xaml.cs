using System.Windows;
using System.Windows.Controls;

using DeepVisionSharp.Shell.Services;
using DeepVisionSharp.Shell.ViewModels.Dashboard;

namespace DeepVisionSharp.Shell.Views.Pages;

public partial class DashboardPage : Page
{
    private const double TitleBarHeight = 44;
    private const double PageVerticalMargin = 56;

    private readonly DashboardViewModel _viewModel;
    private Window? _hostWindow;
    private CancellationTokenSource? _refreshCts;

    public DashboardPage(DashboardViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;

        InitializeComponent();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _hostWindow = Window.GetWindow(this);
        if (_hostWindow is not null)
        {
            _hostWindow.SizeChanged += OnHostWindowSizeChanged;
        }

        UpdateViewportConstraints();

        // Session preserving: the singleton page/VM keep the filter, type and
        // scroll position across navigations. Re-entering refreshes the data silently
        // (no blocking overlay); only the very first load shows the overlay.
        _refreshCts?.Dispose();
        _refreshCts = new CancellationTokenSource();
        try
        {
            if (_viewModel.List.HasProjects)
            {
                await _viewModel.List.RefreshProjectsAsync(_refreshCts.Token);
            }
            else
            {
                await _viewModel.LoadCommand.ExecuteAsync(null);
            }
        }
        catch (OperationCanceledException)
        {
            // Navigated away while (re)loading — the next entry reloads.
        }
        catch (Exception ex)
        {
            // async void: an escaped exception would pop a global error dialog on every navigation.
            UnhandledExceptionReporter.ReportStatic(ex, "DashboardPage.OnLoaded", promptUser: true);
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _viewModel.List.LoadCommand.Cancel();
        _refreshCts?.Cancel();
        _refreshCts?.Dispose();
        _refreshCts = null;

        if (_hostWindow is not null)
        {
            _hostWindow.SizeChanged -= OnHostWindowSizeChanged;
            _hostWindow = null;
        }
    }

    private void OnPageSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateViewportConstraints();
    }

    private void OnHostWindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateViewportConstraints();
    }

    /// <summary>
    /// NavigationView 对 Page 常按无限高度测量，导致整页 ScrollViewer 无法出现滚动条。
    /// 用窗口可视高度限制根布局，使瀑布页获得有限视口高度。
    /// </summary>
    private void UpdateViewportConstraints()
    {
        var window = _hostWindow ?? Window.GetWindow(this);
        if (window is null || window.ActualHeight <= 0)
        {
            return;
        }

        var pageHeight = window.ActualHeight - TitleBarHeight - PageVerticalMargin;
        if (pageHeight <= 0)
        {
            return;
        }

        PageRoot.MaxHeight = pageHeight;
        PageRoot.Height = pageHeight;

        // 响应式列数：按可用宽度在 1–4 列间切换（约每列 ≥380px），卡片 Stretch 均分宽度。
        var availableWidth = PageRoot.ActualWidth;
        _viewModel.CardColumns = availableWidth > 0
            ? Math.Clamp((int)Math.Floor(availableWidth / 380), 1, 4)
            : 1;
    }
}
