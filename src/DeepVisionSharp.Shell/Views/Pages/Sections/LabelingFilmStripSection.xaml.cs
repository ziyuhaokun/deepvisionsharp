using System.Windows;
using System.Windows.Input;

using DeepVisionSharp.Shell.Services;
using DeepVisionSharp.Shell.ViewModels.Labeling;

namespace DeepVisionSharp.Shell.Views.Pages.Sections;

public partial class LabelingFilmStripSection : System.Windows.Controls.UserControl
{
    public LabelingFilmStripSection() => InitializeComponent();

    private LabelingViewModel? ViewModel => DataContext as LabelingViewModel;

    private async void OnSampleTileMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LabelingSampleRow row }
            && ViewModel is { } viewModel
            && viewModel.Samples.SelectSampleCommand.CanExecute(row.Id))
        {
            // Mark handled before awaiting: WPF finishes routing the click as soon as this returns.
            e.Handled = true;
            await GuardAsync(
                () => viewModel.Samples.SelectSampleCommand.ExecuteAsync(row.Id),
                "LabelingFilmStripSection.SelectSample");
        }
    }

    private async void OnDeleteSampleMenuItemClicked(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem { Parent: System.Windows.Controls.ContextMenu { PlacementTarget: FrameworkElement { DataContext: LabelingSampleRow row } } }
            && ViewModel is { } viewModel
            && viewModel.Samples.ConfirmDeleteSampleCommand.CanExecute(row.Id))
        {
            await GuardAsync(
                () => viewModel.Samples.ConfirmDeleteSampleCommand.ExecuteAsync(row.Id),
                "LabelingFilmStripSection.DeleteSample");
        }
    }

    private async void OnSetSampleAnchorMenuItemClicked(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem { Parent: System.Windows.Controls.ContextMenu { PlacementTarget: FrameworkElement { DataContext: LabelingSampleRow row } } }
            && ViewModel is { } viewModel
            && viewModel.Samples.SetSampleAnchorCommand.CanExecute(row))
        {
            await GuardAsync(
                () => viewModel.Samples.SetSampleAnchorCommand.ExecuteAsync(row),
                "LabelingFilmStripSection.SetAnchor");
        }
    }

    private async void OnClearSampleAnchorMenuItemClicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel
            && viewModel.Samples.ClearSampleAnchorCommand.CanExecute(null))
        {
            await GuardAsync(
                () => viewModel.Samples.ClearSampleAnchorCommand.ExecuteAsync(null),
                "LabelingFilmStripSection.ClearAnchor");
        }
    }

    /// <summary>
    /// Runs an async command from an <c>async void</c> handler. Without this an exception escapes to
    /// the dispatcher handler and pops a global error dialog for every click.
    /// </summary>
    private static async Task GuardAsync(Func<Task> action, string source)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            UnhandledExceptionReporter.ReportStatic(ex, source, promptUser: false);
        }
    }
}
