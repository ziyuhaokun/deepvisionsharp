using System.Windows;
using System.Windows.Controls;
using DeepVisionSharp.Shell.Services;
using DeepVisionSharp.Shell.ViewModels.Training;

namespace DeepVisionSharp.Shell.Views.Pages;

public partial class TrainingPage : Page
{
    private const double TitleBarHeight = 44;
    private const double PageVerticalMargin = 56;

    private readonly TrainingViewModel _viewModel;
    private readonly PageDisposalScheduler _disposal;
    private Window? _hostWindow;

    public TrainingPage(TrainingViewModel viewModel)
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

        // The page is being (re-)shown: a dispose scheduled by the previous unload is cancelled.
        _disposal.CancelPendingDispose();

        try
        {
            _viewModel.Activate();
            await _viewModel.JobList.LoadCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            // Last-resort: never let page Loaded crash the process.
            System.Diagnostics.Debug.WriteLine(ex);
            UnhandledExceptionReporter.ReportStatic(ex, "TrainingPage.OnLoaded", promptUser: true);
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // NavigationView frequently unloads/reloads pages. Disposing the Transient ViewModel
        // synchronously here races async LoadAsync and DispatcherTimer teardown and can hard-crash
        // the process with no managed exception log. Cancel work now, dispose later only if the page
        // is not re-shown (the DI root otherwise keeps this view-model graph until exit).
        try
        {
            _viewModel.Deactivate();
        }
        catch
        {
            // ignore deactivation failures on unload
        }

        if (_hostWindow is not null)
        {
            _hostWindow.SizeChanged -= OnHostWindowSizeChanged;
            _hostWindow = null;
        }

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

        PageRootDock.MaxHeight = pageHeight;
    }

    private void OnTrainingJobActionsMenuButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button || button.ContextMenu is null)
        {
            return;
        }

        button.ContextMenu.PlacementTarget = button;
        button.ContextMenu.IsOpen = true;
        e.Handled = true;
    }
}
