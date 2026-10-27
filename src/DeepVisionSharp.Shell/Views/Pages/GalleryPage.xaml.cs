using System.Windows;
using System.Windows.Controls;
using DeepVisionSharp.Shell.Services;
using DeepVisionSharp.Shell.ViewModels;
using DeepVisionSharp.Shell.ViewModels.Gallery;

namespace DeepVisionSharp.Shell.Views.Pages;

public partial class GalleryPage : Page
{
    private const double TitleBarHeight = 44;
    private const double PageVerticalMargin = 56;

    private readonly GalleryViewModel _viewModel;
    private readonly PageDisposalScheduler _disposal;
    private Window? _hostWindow;

    public GalleryPage(GalleryViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        _disposal = new PageDisposalScheduler(
            () => _viewModel,
            ex => System.Diagnostics.Debug.WriteLine(ex));

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

        // Re-shown: cancel the dispose scheduled by the previous unload.
        _disposal.CancelPendingDispose();

        try
        {
            // Always reload so project/session switches refresh the list (no one-shot _loaded guard).
            await _viewModel.LoadCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            // async void: an escaped exception would reach the dispatcher handler and pop a global
            // error dialog on every navigation.
            UnhandledExceptionReporter.ReportStatic(ex, "GalleryPage.OnLoaded", promptUser: true);
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // Cancel work now; dispose later only if the page is not re-shown (the DI root otherwise
        // keeps this view-model graph until the process exits).
        _viewModel.Deactivate();
        _disposal.ScheduleDispose();

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
    /// NavigationView 对 Page 常按无限高度测量，导致独立 ScrollViewer 无法出现滚动条。
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

        // 响应式列数：按可用宽度在 1–4 列间切换（约每列 ≥360px），卡片横向 Stretch 均分宽度。
        var availableWidth = PageRoot.ActualWidth;
        _viewModel.CardColumns = availableWidth > 0
            ? Math.Clamp((int)Math.Floor(availableWidth / 360), 1, 4)
            : 1;
    }
}
