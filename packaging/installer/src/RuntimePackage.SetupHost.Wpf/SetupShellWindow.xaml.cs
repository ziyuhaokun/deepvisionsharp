using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace RuntimePackage.SetupHost;

public partial class SetupShellWindow : global::Wpf.Ui.Controls.FluentWindow
{
    private readonly SetupViewModel _viewModel;

    internal SetupShellWindow(SetupViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = _viewModel;
        InitializeComponent();

        // Follow system theme
        SystemThemeWatcher.Watch(this);

        _viewModel.ConfirmOverwriteAsync = ShowOverwriteConfirmationAsync;
        _viewModel.RequestClose += (_, _) => Close();
        Closed += (_, _) => _viewModel.ReleaseTemporaryFiles();
    }

    private async Task<bool> ShowOverwriteConfirmationAsync(string title, string message)
    {
        var dialog = new ContentDialog(RootContentDialogHost)
        {
            Title = title,
            Content = message,
            PrimaryButtonText = "覆盖安装",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close
        };

        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary;
    }
}
