using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeepVisionSharp.Domain.Datasets;

/// <summary>Shared on-disk contract for an exported classification or detection dataset.</summary>
public sealed record DatasetManifest
{
    public const string FileName = "dataset-manifest.json";
    public const int CurrentVersion = 2;
    public const string Classification = "classify";
    public const string Detection = "detect";

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    // Older exports and fixtures may omit schema_version, task, and the per-sample details.
    public int SchemaVersion { get; init; } = 1;
    public string? Task { get; init; }
    public string[]? Classes { get; init; }
    public string? DatasetFingerprint { get; init; }
    public int? TrainingCount { get; init; }
    public int? ValidationCount { get; init; }
    public int? TrainingBoxes { get; init; }
    public int? ValidationBoxes { get; init; }
    public string? SplitMethod { get; init; }
    public int? SplitSeed { get; init; }
    public int? SplitOverlapCount { get; init; }
    public DatasetManifestSample[]? Samples { get; init; }

    public static DatasetManifest Read(string path, string expectedTask)
    {
        using var stream = File.OpenRead(path);
        var manifest = JsonSerializer.Deserialize<DatasetManifest>(stream, JsonOptions)
            ?? throw new InvalidDataException($"数据集清单格式错误：{path}");
        if (manifest.SchemaVersion == 1)
        {
            return manifest;
        }

        if (manifest.SchemaVersion != CurrentVersion)
        {
            throw new InvalidDataException($"不支持的数据集清单版本 {manifest.SchemaVersion}：{path}");
        }

        if (manifest.Task != expectedTask)
        {
            throw new InvalidDataException($"数据集清单的任务类型应为 {expectedTask}，实际为 {manifest.Task ?? "未指定"}：{path}");
        }

        if (manifest.Classes is not { Length: > 0 }
            || manifest.Classes.Any(string.IsNullOrWhiteSpace)
            || manifest.Classes.Distinct(StringComparer.OrdinalIgnoreCase).Count() != manifest.Classes.Length
            || string.IsNullOrWhiteSpace(manifest.DatasetFingerprint)
            || manifest.TrainingCount is not > 0
            || manifest.ValidationCount is not > 0
            || manifest.Samples is null
            || manifest.Samples.Length != (long)manifest.TrainingCount + manifest.ValidationCount)
        {
            throw new InvalidDataException($"数据集清单缺少必需信息，或样本数不匹配：{path}");
        }

        return manifest;
    }

    public Task WriteAsync(Stream stream, CancellationToken cancellationToken) =>
        JsonSerializer.SerializeAsync(stream, this, JsonOptions, cancellationToken);
}

/// <summary>One exported image assignment. A classification label is represented by its class index.</summary>
public sealed record DatasetManifestSample(
    Guid GalleryId,
    string FileName,
    string Split,
    string ImagePath,
    string ImageSha256,
    int? ClassIndex,
    string? LabelSha256);
