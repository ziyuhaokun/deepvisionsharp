using Microsoft.Win32;
using System.Windows;

namespace RuntimePackage.SetupHost;

public partial class MainWindow : Window
{
    private readonly SetupViewModel _viewModel = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
    }

    public async Task LoadBundledPackageAsync(string setupExecutablePath, CancellationToken cancellationToken)
    {
        await _viewModel.LoadBundledPackageAsync(setupExecutablePath, cancellationToken);
    }

    private async void OnInstall(object sender, RoutedEventArgs e)
    {
        _viewModel.NavigateNext();
        await Task.CompletedTask;
    }

    private void OnBrowseInstallDirectory(object sender, RoutedEventArgs e)
    {
        OpenFolderDialog dialog = new()
        {
            Multiselect = false,
            Title = "选择安装目录"
        };

        if (dialog.ShowDialog() == true)
        {
            _viewModel.InstallDirectory = dialog.FolderName;
        }
    }

    private void OnClose(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
