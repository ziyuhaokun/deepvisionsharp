using System.IO;
using DeepVisionSharp.Application.Models;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Domain.Entities;
using DeepVisionSharp.Domain.ValueObjects;

namespace DeepVisionSharp.Shell.ViewModels.Training;

internal static class TrainingJobActionRules
{
    public static bool CanDuplicate(TrainingJobListItem job) =>
        TrainingJobTransitions.IsTerminal(job.Status);

    /// <summary>续训在当前 1.0 分类任务中不可用。</summary>
    public static bool CanResume(TrainingJobListItem job) => false;

    public static bool CanRename(TrainingJobListItem job) =>
        job.Status == TrainingJobStatus.Draft;

    public static bool CanStop(TrainingJobListItem job, ITrainingProcessHost processHost) =>
        TrainingJobTransitions.IsActive(job.Status) && processHost.IsActive(job.Id);

    /// <summary>
    /// Terminal jobs that still have usable weights on disk (success, cancel, or post-train script failure).
    /// </summary>
    public static bool CanSaveAsModel(TrainingJobListItem job)
    {
        if (job.Status is not (TrainingJobStatus.Succeeded or TrainingJobStatus.Cancelled or TrainingJobStatus.Failed))
        {
            return false;
        }

        return HasAnySaveableCheckpoint(job);
    }

    public static bool CanSaveCheckpoint(TrainingJobListItem job, TrainingCheckpointSelection selection)
    {
        if (!CanSaveAsModel(job))
        {
            return false;
        }

        return ResolveCheckpointPath(job, selection) is not null;
    }

    public static bool HasAnySaveableCheckpoint(TrainingJobListItem job)
        => ResolveCheckpointPath(job, TrainingCheckpointSelection.Best) is not null
           || ResolveCheckpointPath(job, TrainingCheckpointSelection.Last) is not null;

    public static string? ResolveCheckpointPath(TrainingJobListItem job, TrainingCheckpointSelection selection)
    {
        if (selection == TrainingCheckpointSelection.Last)
        {
            if (HasExistingFile(job.LastModelPath))
            {
                return job.LastModelPath;
            }

            return ProbeOutput(job.OutputRoot, isLast: true);
        }

        if (HasExistingFile(job.BestModelPath))
        {
            return job.BestModelPath;
        }

        return ProbeOutput(job.OutputRoot, isLast: false);
    }

    private static bool HasExistingFile(string? path)
        => !string.IsNullOrWhiteSpace(path) && File.Exists(path);

    private static string? ProbeOutput(string? outputRoot, bool isLast)
    {
        if (string.IsNullOrWhiteSpace(outputRoot) || !Directory.Exists(outputRoot))
        {
            return null;
        }

        string[] relatives = isLast
            ?
            [
                "last.torchsharp",
                Path.Combine("train", "weights", "last.pt"),
                "last.pt"
            ]
            :
            [
                "best.torchsharp",
                Path.Combine("train", "weights", "best.pt"),
                "best.pt"
            ];

        foreach (var relative in relatives)
        {
            var candidate = Path.Combine(outputRoot, relative);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public static TrainingCheckpointSelection ResolveDefaultSelection(TrainingJobListItem job)
    {
        var hasBest = ResolveCheckpointPath(job, TrainingCheckpointSelection.Best) is not null;
        var hasLast = ResolveCheckpointPath(job, TrainingCheckpointSelection.Last) is not null;

        if (!hasBest && hasLast)
        {
            return TrainingCheckpointSelection.Last;
        }

        return TrainingCheckpointSelection.Best;
    }

    public static bool SupportsDualCheckpointUi(TrainingJobListItem job) => false;

    public static bool CanOpenOutputFolder(TrainingJobListItem job) =>
        !string.IsNullOrWhiteSpace(job.OutputRoot) && Directory.Exists(job.OutputRoot);

    public static bool CanOpenLogFolder(TrainingJobListItem job) =>
        !string.IsNullOrWhiteSpace(job.LogFile) && File.Exists(job.LogFile);

    public static bool CanShowLogs(TrainingJobListItem job) =>
        job.Status != TrainingJobStatus.Draft;

    /// <summary>
    /// Delete only when status is not Preparing/Running and the process host is idle.
    /// Preparing writes OutputRoot before the host tracks a process — both gates required.
    /// </summary>
    public static bool CanDelete(TrainingJobListItem job, ITrainingProcessHost processHost) =>
        !TrainingJobTransitions.IsActive(job.Status) && !processHost.IsActive(job.Id);
}
