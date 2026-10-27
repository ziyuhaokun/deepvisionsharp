using System.Windows.Controls;

using DeepVisionSharp.Shell.Services;
using DeepVisionSharp.Shell.ViewModels.Models;

namespace DeepVisionSharp.Shell.Views.Pages;

public partial class ModelsPage : Page
{
    private readonly ModelsViewModel _viewModel;
    private readonly PageDisposalScheduler _disposal;

    public ModelsPage(ModelsViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        _disposal = new PageDisposalScheduler(
            () => _viewModel,
            ex => System.Diagnostics.Debug.WriteLine(ex));

        InitializeComponent();
    }

    private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        _disposal.CancelPendingDispose();
        try
        {
            await _viewModel.OnPageLoadedAsync();
        }
        catch (Exception ex)
        {
            // async void: an escaped exception would pop a global error dialog on every navigation.
            UnhandledExceptionReporter.ReportStatic(ex, "ModelsPage.OnLoaded", promptUser: true);
        }
    }

    private void OnUnloaded(object sender, System.Windows.RoutedEventArgs e)
    {
        // Cancel work now; dispose later only if the page is not re-shown.
        _viewModel.Deactivate();
        _disposal.ScheduleDispose();
    }
}
