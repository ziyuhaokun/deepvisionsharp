using CommunityToolkit.Mvvm.ComponentModel;
using DeepVisionSharp.Shell.Services;

namespace DeepVisionSharp.Shell.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly WorkspaceSessionService _workspaceSession;

    [ObservableProperty]
    private string _title = "DeepVisionSharp 深度学习平台";

    [ObservableProperty]
    private string _currentProjectName = string.Empty;

    public bool HasCurrentProject => !string.IsNullOrEmpty(CurrentProjectName);

    public MainWindowViewModel(
        WorkspaceSessionService workspaceSession)
    {
        _workspaceSession = workspaceSession;

        SyncCurrentProjectName();
        _workspaceSession.Changed += OnWorkspaceSessionChanged;
    }

    private void OnWorkspaceSessionChanged(object? sender, WorkspaceSessionChangedEventArgs e)
    {
        SyncCurrentProjectName();
    }

    private void SyncCurrentProjectName()
    {
        CurrentProjectName = _workspaceSession.Current.Project?.Name ?? string.Empty;
        OnPropertyChanged(nameof(HasCurrentProject));
    }

    partial void OnCurrentProjectNameChanged(string value)
    {
        OnPropertyChanged(nameof(HasCurrentProject));
    }
}
