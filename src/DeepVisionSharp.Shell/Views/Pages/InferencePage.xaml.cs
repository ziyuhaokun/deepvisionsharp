using System.Windows;
using System.Windows.Controls;
using DeepVisionSharp.Shell.Services;
using DeepVisionSharp.Shell.ViewModels.Inference;

namespace DeepVisionSharp.Shell.Views.Pages;

public partial class InferencePage : Page
{
    /// <summary>Matches MainWindow title bar height.</summary>
    private const double TitleBarHeight = 44;

    private readonly InferenceViewModel _viewModel;
    private readonly PageDisposalScheduler _disposal;
    private Window? _hostWindow;

    public InferencePage(InferenceViewModel viewModel)
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
        _disposal.CancelPendingDispose();
        try
        {
            // Re-attaches singleton subscriptions: NavigationView may unload and re-show this page.
            _viewModel.Activate();
            await _viewModel.OnPageLoadedAsync();
        }
        catch (Exception ex)
        {
            // async void: without this an exception escapes to the dispatcher handler and pops a
            // global error dialog on every navigation.
            UnhandledExceptionReporter.ReportStatic(ex, "InferencePage.OnLoaded", promptUser: true);
        }

        UpdateViewportConstraints();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_hostWindow is not null)
        {
            _hostWindow.SizeChanged -= OnHostWindowSizeChanged;
            _hostWindow = null;
        }

        // Deactivate now; dispose later only if the page is not re-shown.
        _viewModel.Deactivate();
        _disposal.ScheduleDispose();
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
    /// Cap the page to the host window content height so NavigationView cannot measure
    /// this page at infinite height (which would let the ListBox expand the whole UI).
    /// </summary>
    private void UpdateViewportConstraints()
    {
        var window = _hostWindow ?? Window.GetWindow(this);
        if (window is null || window.ActualHeight <= 0)
        {
            return;
        }

        // Height is content box only; Margin sits outside it — subtract both or the
        // page overshoots / leaves a dead band under the status bar.
        var verticalMargin = PageRoot.Margin.Top + PageRoot.Margin.Bottom;
        var pageHeight = window.ActualHeight - TitleBarHeight - verticalMargin;
        if (pageHeight <= 0)
        {
            return;
        }

        PageRoot.MaxHeight = pageHeight;
        PageRoot.Height = pageHeight;
        ImageListSection.UpdateListHeight();
    }
}

