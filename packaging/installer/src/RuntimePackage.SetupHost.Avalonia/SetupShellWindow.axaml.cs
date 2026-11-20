using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace RuntimePackage.SetupHost;

public partial class SetupShellWindow : Window
{
    internal SetupViewModel ViewModel { get; } = new();

    public SetupShellWindow()
    {
        InitializeComponent();
        DataContext = ViewModel;
        ViewModel.ConfirmOverwriteAsync = ShowOverwriteConfirmationAsync;
        ViewModel.RequestClose += (_, _) => Close();
        Closing += (_, args) => args.Cancel = ViewModel.IsInstalling;
        Closed += (_, _) => ViewModel.ReleaseTemporaryFiles();
    }

    private Task<bool> ShowOverwriteConfirmationAsync(string title, string message) =>
        new SetupNoticeWindow(title, message, "覆盖安装").ShowDialog<bool>(this);

    private async void BrowseInstallDirectory(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择安装目录"
        });
        if (folders.FirstOrDefault()?.TryGetLocalPath() is { } path)
        {
            ViewModel.InstallDirectory = path;
        }
    }

    private void ScrollLogToEnd(object? sender, SizeChangedEventArgs e)
    {
        if (sender is ItemsControl { Parent: ScrollViewer scrollViewer })
        {
            scrollViewer.ScrollToEnd();
        }
    }
}
