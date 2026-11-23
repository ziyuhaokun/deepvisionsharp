using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using DeepVisionSharp.Application.Models;
using DeepVisionSharp.Application.Training;
using DeepVisionSharp.Domain.Entities;
using DeepVisionSharp.Presentation.Automation;
using DeepVisionSharp.Presentation.ViewModels.Training;

namespace DeepVisionSharp.Presentation.ViewModels;

internal static class TrainingJobUi
{
    public static bool IsActiveStatus(TrainingJobStatus status) =>
        TrainingJobTransitions.IsActive(status);

    public static string GetStatusDisplay(TrainingJobStatus status) =>
        status switch
        {
            TrainingJobStatus.Draft => "草稿",
            TrainingJobStatus.Preparing => "准备中",
            TrainingJobStatus.Running => "训练中",
            TrainingJobStatus.Succeeded => "已完成",
            TrainingJobStatus.Failed => "失败",
            TrainingJobStatus.Cancelled => "已取消",
            _ => status.ToString()
        };

    public static TrainingJobListItem MapListItem(TrainingJobRow row)
    {
        var item = new TrainingJobListItem
        {
            Id = row.Id,
            Name = row.Name,
            ModelType = row.ModelType,
            Status = row.Status,
            StatusDisplay = GetStatusDisplay(row.Status),
            StartedAt = row.StartedAt,
            FinishedAt = row.FinishedAt,
            LogFile = row.LogFile,
            BestModelPath = row.BestModelPath,
            LastModelPath = row.LastModelPath,
            OutputRoot = row.OutputRoot,
            FailureMessage = row.FailureMessage,
            Source = row
        };
        item.RefreshMetaText();
        return item;
    }
}

public sealed partial class TrainingJobListItem : ObservableObject
{
    public Guid Id { get; init; }

    public string AutomationId => UiAutomationIds.Training.Job(Id);

    [ObservableProperty]
    private string _name = string.Empty;

    public string ModelType { get; init; } = ModelTypeNames.Default;

    public TrainingJobStatus Status { get; init; }

    public string StatusDisplay { get; init; } = string.Empty;

    [ObservableProperty]
    private string _metaText = string.Empty;

    /// <summary>列表卡片里的「开始时间」胶囊文案；没开始时是「尚未开始」。</summary>
    [ObservableProperty]
    private string _startedText = string.Empty;

    /// <summary>列表卡片里的「用时」胶囊文案；没开始时为空（胶囊隐藏）。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDuration))]
    private string _durationText = string.Empty;

    public bool HasDuration => !string.IsNullOrEmpty(DurationText);

    public DateTime? StartedAt { get; init; }

    public DateTime? FinishedAt { get; init; }

    public string? LogFile { get; init; }

    public string? BestModelPath { get; init; }

    public string? LastModelPath { get; init; }

    public string? OutputRoot { get; init; }

    public string? FailureMessage { get; init; }

    public TrainingJobRow Source { get; init; } = null!;

    public bool CanDuplicate => TrainingJobActionRules.CanDuplicate(this);

    public bool CanRename => TrainingJobActionRules.CanRename(this);

    public bool CanSaveAsModel => TrainingJobActionRules.CanSaveAsModel(this);

    public bool CanShowLogs => TrainingJobActionRules.CanShowLogs(this);

    public bool CanStop => TrainingJobTransitions.IsActive(Status);

    public bool CanDelete => !TrainingJobTransitions.IsActive(Status);

    public bool CanOpenOutputFolder =>
        !string.IsNullOrWhiteSpace(OutputRoot) && Directory.Exists(OutputRoot);

    public bool CanOpenLogFolder =>
        !string.IsNullOrWhiteSpace(LogFile) && File.Exists(LogFile);

    public void RefreshMetaText()
    {
        MetaText = TrainingJobTiming.FormatMetaText(StartedAt, FinishedAt);
        if (StartedAt is null)
        {
            StartedText = "尚未开始";
            DurationText = string.Empty;
            return;
        }

        StartedText = TrainingJobTiming.FormatStartedAtLocal(StartedAt);
        DurationText = TrainingJobTiming.FormatDuration(TrainingJobTiming.ComputeElapsed(StartedAt, FinishedAt));
    }
}



