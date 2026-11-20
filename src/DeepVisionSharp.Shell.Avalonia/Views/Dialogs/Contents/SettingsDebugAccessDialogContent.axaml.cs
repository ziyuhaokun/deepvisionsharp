using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace DeepVisionSharp.Shell.Avalonia.Views.Dialogs.Contents;

public partial class SettingsDebugAccessDialogContent : UserControl
{
    public SettingsDebugAccessDialogContent()
    {
        InitializeComponent();
        Loaded += OnContentLoaded;
    }

    private void OnContentLoaded(object? sender, RoutedEventArgs e)
    {
        Dispatcher.UIThread.Post(() => PasswordInput.Focus(), DispatcherPriority.Input);
    }
}
