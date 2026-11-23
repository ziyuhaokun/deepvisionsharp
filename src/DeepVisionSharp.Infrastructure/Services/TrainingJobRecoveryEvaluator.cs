using System.Text;
using System.Text.Json;
using DeepVisionSharp.Application.Models;
using Microsoft.Extensions.Logging;
using DeepVisionSharp.Domain.Entities;

namespace DeepVisionSharp.Infrastructure.Services;

internal static class TrainingJobRecoveryEvaluator
{
    private const int LogTailBytes = 256 * 1024;

    public static TrainingRecoveryDecision Evaluate(TrainingJob job, string interruptionMessage, ILogger logger)
    {
        try
        {
            var record = TrainingCompletionRecord.Read(job);
            if (record is not null)
            {
                return new(TrainingRecoveryAction.MarkSucceeded,
                    record.BestPath(job.OutputRoot!), record.LastPath(job.OutputRoot!), CompletedUtc: record.CompletedUtc);
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or ArgumentException or NotSupportedException)
        {
            return new(TrainingRecoveryAction.MarkFailed, Reason: $"训练完成记录无效：{ex.Message}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Cannot read training completion record for {JobId}; will retry", job.Id);
            return new(TrainingRecoveryAction.KeepRunning);
        }

        var expectedTask = ModelTypeNames.IsDetectionModelType(job.ModelType) ? "detect" : "classify";
        var legacy = ReadLegacyCompletion(job.LogFile, job.OutputRoot, expectedTask);
        if (legacy is not null)
        {
            // Keep the historical optional last checkpoint, but use the exact best from the log.
            var last = legacy.LastModelPath is null ? TryResolveLastModelPath(job)
                : File.Exists(legacy.LastModelPath) ? legacy.LastModelPath : null;
            return new(TrainingRecoveryAction.MarkSucceeded, legacy.BestModelPath, last);
        }

        var marker = TrainingRunMarkerStore.TryRead(job.OutputRoot, logger);
        return new(TrainingRecoveryAction.MarkFailed,
            Reason: TrainingRunMarkerStore.BuildInterruptionMessage(marker, job.Id, interruptionMessage));
    }

    public static bool ShouldDeferTerminalWrite(TrainingJob job)
    {
        try
        {
            return TrainingCompletionRecord.Read(job) is not null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An unreadable record may still describe a completed run. Leave it for reconciliation.
            return true;
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    public static string? TryResolveBestModelPath(TrainingJob job)
    {
        if (!string.IsNullOrWhiteSpace(job.BestModelPath) && File.Exists(job.BestModelPath))
        {
            return job.BestModelPath;
        }

        return TrainingCheckpointResolver.ResolveBestFromOutputDirectory(job.OutputRoot);
    }

    public static string? TryResolveLastModelPath(TrainingJob job)
    {
        if (!string.IsNullOrWhiteSpace(job.LastModelPath) && File.Exists(job.LastModelPath))
        {
            return job.LastModelPath;
        }

        return TrainingCheckpointResolver.ResolveLastFromOutputDirectory(job.OutputRoot);
    }

    private static TrainingCompletedPayload? ReadLegacyCompletion(string? logFile, string? outputRoot, string expectedTask)
    {
        if (string.IsNullOrWhiteSpace(logFile) || !File.Exists(logFile))
        {
            return null;
        }

        var workingDirectory = string.IsNullOrWhiteSpace(outputRoot) ? AppContext.BaseDirectory : Path.GetFullPath(outputRoot);
        var lines = ReadLogTail(logFile).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines.Reverse())
        {
            if (TrainingCompletionSignals.TryParseCompleted(line, workingDirectory, out var payload)
                && payload is not null
                && (string.IsNullOrWhiteSpace(payload.Task) || string.Equals(payload.Task, expectedTask, StringComparison.OrdinalIgnoreCase))
                && IsWithinOutputRoot(payload.BestModelPath, outputRoot)
                && string.Equals(payload.MetadataPath, payload.BestModelPath + ".info", StringComparison.OrdinalIgnoreCase)
                && File.Exists(payload.BestModelPath) && File.Exists(payload.MetadataPath))
            {
                return IsWithinOutputRoot(payload.LastModelPath, outputRoot) ? payload : payload with { LastModelPath = null };
            }
        }

        return null;
    }

    private static bool IsWithinOutputRoot(string? path, string? outputRoot)
    {
        if (path is null || string.IsNullOrWhiteSpace(outputRoot))
        {
            return true;
        }

        var relative = Path.GetRelativePath(outputRoot, path);
        return !Path.IsPathRooted(relative) && relative != ".."
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static string ReadLogTail(string logFile)
    {
        try
        {
            using var stream = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var offset = Math.Max(0, stream.Length - LogTailBytes);
            stream.Seek(offset, SeekOrigin.Begin);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }
        catch
        {
            return string.Empty;
        }
    }
}
