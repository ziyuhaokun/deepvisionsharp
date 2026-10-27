using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using DeepVisionSharp.Shell.Services.Tasks;

namespace DeepVisionSharp.Shell.ViewModels;

public sealed partial class BackgroundTaskRowViewModel : ObservableObject
{
    private BackgroundTaskStatus _status = BackgroundTaskStatus.Queued;

    public BackgroundTaskRowViewModel(Guid id, BackgroundTaskKind kind, string title)
    {
        Id = id;
        Kind = kind;
        Title = title;
        StartedAt = DateTimeOffset.Now;
    }

    public Guid Id { get; }

    public BackgroundTaskKind Kind { get; }

    public string Title { get; }

    public DateTimeOffset StartedAt { get; }

    public DateTimeOffset? FinishedAt { get; private set; }

    public string? Error { get; private set; }

    public ObservableCollection<string> LogLines { get; } = new();

    public BackgroundTaskStatus Status
    {
        get => _status;
        private set
        {
            if (SetProperty(ref _status, value))
            {
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(IsRunning));
                OnPropertyChanged(nameof(IsSucceeded));
                OnPropertyChanged(nameof(IsFailed));
            }
        }
    }

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isVisible = true;

    public bool IsRunning => Status == BackgroundTaskStatus.Running;

    public bool IsSucceeded => Status == BackgroundTaskStatus.Succeeded;

    public bool IsFailed => Status is BackgroundTaskStatus.Failed or BackgroundTaskStatus.Cancelled;

    public string StatusText => Status switch
    {
        BackgroundTaskStatus.Queued => "排队中",
        BackgroundTaskStatus.Running => "运行中",
        BackgroundTaskStatus.Succeeded => "已完成",
        BackgroundTaskStatus.Failed => "失败",
        BackgroundTaskStatus.Cancelled => "已取消",
        _ => Status.ToString()
    };

    public void MarkRunning()
    {
        Status = BackgroundTaskStatus.Running;
    }

    public void MarkCompleted(bool success, string? error)
    {
        FinishedAt = DateTimeOffset.Now;
        Error = error;
        Status = success ? BackgroundTaskStatus.Succeeded : BackgroundTaskStatus.Failed;
    }
}
