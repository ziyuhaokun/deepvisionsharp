using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using DeepVisionSharp.Shell.Services;
using DeepVisionSharp.Shell.ViewModels.Labeling;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace DeepVisionSharp.Shell.Views.Pages;

public partial class LabelingPage : Page
{
    private readonly LabelingViewModel _viewModel;
    private bool _shortcutInFlight;

    public LabelingPage(LabelingViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;

        InitializeComponent();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.LoadCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            // async void: an escaped exception would pop a global error dialog on every navigation.
            UnhandledExceptionReporter.ReportStatic(ex, "LabelingPage.OnLoaded", promptUser: true);
        }
    }

    private async void OnUnloaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.DeactivateAsync();
        }
        catch (Exception ex)
        {
            // DeactivateAsync already logs its own failures; this only guards unexpected throws.
            UnhandledExceptionReporter.ReportStatic(ex, "LabelingPage.OnUnloaded", promptUser: false);
        }
    }

    private async void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Mark handled before awaiting: WPF finishes routing this key event as soon as the handler
        // returns, so setting e.Handled afterwards has no effect and the ListBox would also react.
        if (_shortcutInFlight)
        {
            e.Handled = true;
            return;
        }

        _shortcutInFlight = true;
        try
        {
            // Held arrow keys fire repeatedly; without this guard every repeat starts a concurrent
            // navigation that reads the same active index and only advances one sample.
            if (await _viewModel.TryHandleKeyboardShortcutAsync(e.Key, Keyboard.Modifiers))
            {
                e.Handled = true;
            }
        }
        catch (Exception ex)
        {
            UnhandledExceptionReporter.ReportStatic(ex, "LabelingPage.OnPreviewKeyDown", promptUser: false);
        }
        finally
        {
            _shortcutInFlight = false;
        }
    }
}
