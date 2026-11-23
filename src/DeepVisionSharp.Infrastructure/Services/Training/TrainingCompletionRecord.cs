using System.Text.Json;
using DeepVisionSharp.Application.Models;
using DeepVisionSharp.Domain.Entities;
using DeepVisionSharp.TorchSharp.IO;

namespace DeepVisionSharp.Infrastructure.Services;

/// <summary>Durable evidence of a successful run, published before the terminal SQLite update.</summary>
internal sealed record TrainingCompletionRecord(
    int SchemaVersion,
    Guid JobId,
    string ModelType,
    DateTime CompletedUtc,
    string BestCheckpoint,
    string LastCheckpoint)
{
    internal const string FileName = "training-completed.json";
    private const int CurrentSchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true
    };

    public static TrainingCompletionRecord Publish(
        TrainingJob job, string bestCheckpoint, string bestMetadata, string lastCheckpoint, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var outputRoot = GetOutputRoot(job);
        var bestRelative = Path.GetRelativePath(outputRoot, Path.GetFullPath(bestCheckpoint));
        var lastRelative = Path.GetRelativePath(outputRoot, Path.GetFullPath(lastCheckpoint));
        var record = new TrainingCompletionRecord(
            CurrentSchemaVersion, job.Id, job.ModelType, DateTime.UtcNow, bestRelative, lastRelative);
        if (!string.Equals(Path.GetFullPath(bestMetadata), record.BestPath(outputRoot) + ".info", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("最佳 checkpoint 与 .info 路径不配套。");
        }

        record.Validate(job, outputRoot);
        // The atomic publication is the commit point. The caller must finish persisting success
        // without using the run token once the record has been published.
        AtomicFileWriter.WriteAllText(Path.Combine(outputRoot, FileName), JsonSerializer.Serialize(record, JsonOptions), cancellationToken);
        return record;
    }

    public static TrainingCompletionRecord? Read(TrainingJob job)
    {
        if (string.IsNullOrWhiteSpace(job.OutputRoot))
        {
            return null;
        }

        var outputRoot = GetOutputRoot(job);
        var path = Path.Combine(outputRoot, FileName);
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            // A directory occupying the record path is a broken record, not a legacy run.
            if (Directory.Exists(path))
            {
                throw new InvalidDataException("训练完成记录路径不是文件。", ex);
            }

            return null;
        }

        var record = JsonSerializer.Deserialize<TrainingCompletionRecord>(json, JsonOptions)
            ?? throw new InvalidDataException("训练完成记录为空。");
        record.Validate(job, outputRoot);
        return record;
    }

    public string BestPath(string outputRoot) => Path.GetFullPath(Path.Combine(outputRoot, BestCheckpoint));

    public string LastPath(string outputRoot) => Path.GetFullPath(Path.Combine(outputRoot, LastCheckpoint));

    private void Validate(TrainingJob job, string outputRoot)
    {
        if (SchemaVersion != CurrentSchemaVersion || JobId == Guid.Empty || JobId != job.Id
            || !SameModelType(ModelType, job.ModelType) || CompletedUtc.Kind != DateTimeKind.Utc)
        {
            throw new InvalidDataException("完成记录的版本、任务、模型类型或完成时间无效。");
        }

        // Both in-process trainers publish these two names at the root of a unique run directory.
        // Using the known relative paths also prevents sibling-run and traversal references.
        if (BestCheckpoint != "best.torchsharp" || LastCheckpoint != "last.torchsharp")
        {
            throw new InvalidDataException("完成记录中的 checkpoint 路径必须是本次训练目录的 best.torchsharp 和 last.torchsharp。");
        }

        var expectedTask = ModelTypeNames.IsDetectionModelType(job.ModelType) ? "detect" : "classify";
        var best = ValidateCheckpoint(BestPath(outputRoot), job, expectedTask);
        var last = ValidateCheckpoint(LastPath(outputRoot), job, expectedTask);
        if (best.Fingerprint != last.Fingerprint || !best.Classes.SequenceEqual(last.Classes, StringComparer.Ordinal))
        {
            throw new InvalidDataException("best 与 last 的数据集指纹或类别顺序不一致。");
        }
    }

    private static (string Fingerprint, string[] Classes) ValidateCheckpoint(string checkpoint, TrainingJob job, string task)
    {
        var metadata = checkpoint + ".info";
        ValidateFile(checkpoint);
        ValidateFile(metadata);
        using var document = JsonDocument.Parse(File.ReadAllText(metadata));
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || GetString(root, "task") != task
            || GetString(root, "artifact_kind") != "trained_model"
            || !SameModelType(GetString(root, "model_type"), job.ModelType)
            || !root.TryGetProperty("training_job_id", out var id) || id.ValueKind != JsonValueKind.String
            || !id.TryGetGuid(out var artifactJobId) || artifactJobId != job.Id
            || GetString(root, "weights_format") != "torchsharp"
            || string.IsNullOrWhiteSpace(GetString(root, "arch"))
            || string.IsNullOrWhiteSpace(GetString(root, "preprocessing"))
            || !HasPositiveInteger(root, "input_width") || !HasPositiveInteger(root, "input_height")
            || string.IsNullOrWhiteSpace(GetString(root, "dataset_fingerprint"))
            || !root.TryGetProperty("classes", out var classes) || classes.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException($"{Path.GetFileName(metadata)} 与本次训练不匹配。");
        }

        var names = classes.EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString()! : string.Empty).ToArray();
        if (names.Length < (task == "detect" ? 1 : 2) || names.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidDataException($"{Path.GetFileName(metadata)} 的类别列表无效。");
        }

        return (GetString(root, "dataset_fingerprint")!, names);
    }

    private static void ValidateFile(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists || (file.Attributes & FileAttributes.ReparsePoint) != 0 || file.Length == 0)
        {
            throw new InvalidDataException($"训练产物缺失、为空或是文件链接：{Path.GetFileName(path)}。");
        }
    }

    private static string GetOutputRoot(TrainingJob job) => !string.IsNullOrWhiteSpace(job.OutputRoot)
        ? Path.GetFullPath(job.OutputRoot)
        : throw new InvalidDataException("任务缺少训练输出目录。");

    private static bool SameModelType(string? actual, string? expected) =>
        ModelTypeNames.TryNormalize(actual, out var normalizedActual)
        && ModelTypeNames.TryNormalize(expected, out var normalizedExpected)
        && normalizedActual == normalizedExpected;

    private static bool HasPositiveInteger(JsonElement element, string key) =>
        element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out var number) && number > 0;

    private static string? GetString(JsonElement element, string key) =>
        element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
