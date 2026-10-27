using System.Windows;
using System.Windows.Controls;
using DeepVisionSharp.Shell.Services;
using DeepVisionSharp.Shell.ViewModels;

namespace DeepVisionSharp.Shell.Views.Pages;

public partial class SettingsPage : Page
{
    private const double TitleBarHeight = 44;
    private const double PageVerticalMargin = 56;

    private readonly SettingsViewModel _viewModel;
    private bool _loaded;
    private Window? _hostWindow;

    public SettingsPage(SettingsViewModel viewModel)
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

        if (_loaded)
        {
            return;
        }

        _loaded = true;
        try
        {
            await _viewModel.LoadCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            // async void: an escaped exception would pop a global error dialog on every navigation.
            UnhandledExceptionReporter.ReportStatic(ex, "SettingsPage.OnLoaded", promptUser: true);
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
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
    /// NavigationView 对 Page 常按无限高度测量，导致内层 ScrollViewer 无法出现滚动条。
    /// 用窗口可视高度限制根布局，使内容区获得有限高度。
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
    }
}
