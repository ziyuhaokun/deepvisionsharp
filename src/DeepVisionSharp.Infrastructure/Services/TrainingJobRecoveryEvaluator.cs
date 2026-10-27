using System.Text;
using DeepVisionSharp.Domain.Entities;

namespace DeepVisionSharp.Infrastructure.Services;

internal static class TrainingJobRecoveryEvaluator
{
    private const int LogTailBytes = 256 * 1024;

    public static bool CanRecoverAsSucceeded(TrainingJob job, out string? bestModelPath, out string? lastModelPath)
    {
        bestModelPath = null;
        lastModelPath = null;
        if (!LogFileIndicatesCompleted(job.LogFile, job.OutputRoot))
        {
            return false;
        }

        bestModelPath = TryResolveBestModelPath(job);
        lastModelPath = TryResolveLastModelPath(job);
        return !string.IsNullOrWhiteSpace(bestModelPath)
               && File.Exists(bestModelPath + ".info");
    }

    public static bool CanRecoverAsSucceeded(TrainingJob job, out string? bestModelPath)
    {
        var recovered = CanRecoverAsSucceeded(job, out bestModelPath, out _);
        return recovered;
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

    public static bool LogFileIndicatesCompleted(string? logFile, string? outputRoot)
    {
        if (string.IsNullOrWhiteSpace(logFile) || !File.Exists(logFile))
        {
            return false;
        }

        var text = ReadLogTail(logFile);
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (TrainingCompletionSignals.TryParseCompleted(
                    line,
                    string.IsNullOrWhiteSpace(outputRoot) ? AppContext.BaseDirectory : outputRoot,
                    out var payload)
                && payload is not null
                && File.Exists(payload.BestModelPath)
                && File.Exists(payload.MetadataPath))
            {
                return true;
            }
        }

        return false;
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
