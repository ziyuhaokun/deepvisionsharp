using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using DeepVisionSharp.Shell.Services;
using DeepVisionSharp.Shell.Services.Tasks;

namespace DeepVisionSharp.Shell.ViewModels;

public sealed partial class ShellTaskCenterViewModel : ObservableObject
{
    private const int MaxLogLinesPerTask = 500;

    private readonly IUiDispatcher _uiDispatcher;

    public ShellTaskCenterViewModel(IUiDispatcher uiDispatcher)
    {
        _uiDispatcher = uiDispatcher;
    }

    public ObservableCollection<BackgroundTaskRowViewModel> Tasks { get; } = new();

    [ObservableProperty]
    private bool _isExpanded;

    public int RunningTaskCount => Tasks.Count(task => task.IsVisible && task.IsRunning);

    public int VisibleTaskCount => Tasks.Count(task => task.IsVisible);

    public bool HasVisibleTasks => VisibleTaskCount > 0;

    public bool HasRunningTasks => RunningTaskCount > 0;

    public string HeaderSummary => HasRunningTasks
        ? $"{VisibleTaskCount} 项 · {RunningTaskCount} 运行中"
        : $"{VisibleTaskCount} 项";

    public string ExpandToggleToolTip => IsExpanded ? "收起任务列表" : "展开任务列表";

    public Guid StartTask(BackgroundTaskKind kind, string title)
    {
        var task = new BackgroundTaskRowViewModel(Guid.NewGuid(), kind, title);
        task.MarkRunning();
        _uiDispatcher.Post(() =>
        {
            Tasks.Insert(0, task);
            IsExpanded = true;
            NotifyCountsChanged();
        });
        return task.Id;
    }

    public void AppendLog(Guid taskId, string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        _uiDispatcher.Post(() =>
        {
            var task = FindTask(taskId);
            if (task is null)
            {
                return;
            }

            task.LogLines.Add(line.TrimEnd());
            while (task.LogLines.Count > MaxLogLinesPerTask)
            {
                task.LogLines.RemoveAt(0);
            }
        });
    }

    public void CompleteTask(Guid taskId, bool success, string? error = null)
    {
        _uiDispatcher.Post(() =>
        {
            var task = FindTask(taskId);
            if (task is null)
            {
                return;
            }

            task.MarkCompleted(success, error);
            if (!string.IsNullOrWhiteSpace(error))
            {
                task.LogLines.Add($"[ERR] {error.Trim()}");
            }

            NotifyCountsChanged();
        });
    }

    [RelayCommand]
    private void ToggleExpanded()
    {
        IsExpanded = !IsExpanded;
    }

    [RelayCommand]
    private void DismissTask(Guid taskId)
    {
        var task = FindTask(taskId);
        if (task is null)
        {
            return;
        }

        task.IsVisible = false;
        task.IsExpanded = false;
        NotifyCountsChanged();
    }

    [RelayCommand]
    private void ToggleTaskExpanded(Guid taskId)
    {
        var task = FindTask(taskId);
        if (task is null)
        {
            return;
        }

        task.IsExpanded = !task.IsExpanded;
    }

    private BackgroundTaskRowViewModel? FindTask(Guid taskId)
        => Tasks.FirstOrDefault(task => task.Id == taskId);

    partial void OnIsExpandedChanged(bool value)
    {
        OnPropertyChanged(nameof(ExpandToggleToolTip));
    }

    private void NotifyCountsChanged()
    {
        OnPropertyChanged(nameof(RunningTaskCount));
        OnPropertyChanged(nameof(VisibleTaskCount));
        OnPropertyChanged(nameof(HasVisibleTasks));
        OnPropertyChanged(nameof(HasRunningTasks));
        OnPropertyChanged(nameof(HeaderSummary));
    }
}
