using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using DeepVisionSharp.Domain.ValueObjects;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Infrastructure.Persistence;
using DeepVisionSharp.TorchSharp.IO;

namespace DeepVisionSharp.Infrastructure.Services;

internal sealed class ModelMetadataService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ILogger<ModelMetadataService> _logger;
    private readonly SqliteDb _sqliteDb;
    private readonly IProjectFileStore _fileStore;

    public ModelMetadataService(SqliteDb sqliteDb, IProjectFileStore fileStore, ILogger<ModelMetadataService> logger)
    {
        _logger = logger;
        _sqliteDb = sqliteDb;
        _fileStore = fileStore;
    }

    public ModelMetadata? ReadMetadata(string checkpointPath)
    {
        try
        {
            var infoPath = checkpointPath + ".info";
            if (!File.Exists(infoPath))
            {
                return null;
            }

            var json = File.ReadAllText(infoPath);
            return JsonSerializer.Deserialize<ModelMetadata>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read model metadata from {CheckpointPath}", checkpointPath);
            return null;
        }
    }

    public async Task WriteMetadataAsync(string checkpointPath, ModelMetadata metadata, CancellationToken cancellationToken = default)
    {
        SqlProjectExecutionGuard.EnsurePathWritable(_sqliteDb.Instance, _fileStore, checkpointPath);
        var infoPath = checkpointPath + ".info";
        var json = JsonSerializer.Serialize(metadata, JsonOptions);
        await AtomicFileWriter.WriteAllTextAsync(infoPath, json, cancellationToken);
        _logger.LogInformation("Wrote model metadata to {InfoPath}", infoPath);
    }
}

public sealed class ModelMetadata
{
    public int SchemaVersion { get; set; } = 2;

    public string ArtifactKind { get; set; } = "trained_model";

    public string ModelType { get; set; } = string.Empty;

    public List<string> Classes { get; set; } = new();

    public int MaxDetections { get; set; }

    public int NumClasses { get; set; }

    /// <summary>Classification (and some trainers) store network input edge length at top level.</summary>
    public int InputSize { get; set; }

    /// <summary>Model input channels: 1 grayscale or 3 RGB.</summary>
    public int InputChannels { get; set; } = 3;

    /// <summary>Optional rectangular input; when unset, both sides equal <see cref="InputSize"/>.</summary>
    public int InputWidth { get; set; }

    public int InputHeight { get; set; }

    public string? DatasetFingerprint { get; set; }

    public int Epochs { get; set; }

    public int BatchSize { get; set; }

    public double Lr { get; set; }

    public string Optimizer { get; set; } = string.Empty;

    public string Scheduler { get; set; } = string.Empty;

    public double BestMap { get; set; }

    public double BestMap50 { get; set; }

    /// <summary>Legacy sidecar metrics kept for reading older checkpoints (snake_case: box_map_50).</summary>
    [JsonPropertyName("box_map_50")]
    public double? BoxMap50 { get; set; }

    [JsonPropertyName("box_map_50_95")]
    public double? BoxMap5095 { get; set; }

    [JsonPropertyName("mask_map_50")]
    public double? MaskMap50 { get; set; }

    [JsonPropertyName("mask_map_50_95")]
    public double? MaskMap5095 { get; set; }

    /// <summary>UNet semantic trained sidecar metrics.</summary>
    [JsonPropertyName("miou")]
    public double? Miou { get; set; }

    [JsonPropertyName("dice")]
    public double? Dice { get; set; }

    [JsonPropertyName("pixel_accuracy")]
    public double? PixelAccuracy { get; set; }

    public string? CreatedAt { get; set; }

    public string? Version { get; set; }

    public string? Notes { get; set; }

    public bool IsInitial { get; set; }

    public string? SourceWeight { get; set; }

    /// <summary>
    /// Network architecture key (for example <c>resnet18_v2</c>). The inference engine rebuilds the
    /// module from this before loading weights, so a sidecar without it falls back to resnet18 and
    /// fails to load a resnet34 checkpoint.
    /// </summary>
    public string? Arch { get; set; }

    /// <summary>
    /// Input normalisation used during training (<c>unit_scale_v1</c> / <c>imagenet_v2</c>).
    /// A missing value falls back to unit-scale, which silently mispredicts for ImageNet-pretrained
    /// models.
    /// </summary>
    public string? Preprocessing { get; set; }

    /// <summary>Weight container format; <c>torchsharp</c> for checkpoints written by the trainer.</summary>
    public string? WeightsFormat { get; set; }

    /// <summary>
    /// UI/helper only — must not appear in .pt.info sidecar JSON.
    /// System.Text.Json serializes get-only properties by default; without JsonIgnore this
    /// becomes <c>has_training_metrics</c> and fails legacy schema-v2 classification validation
    /// (unknown field) after cancelled-job synthesized save.
    /// </summary>
    [JsonIgnore]
    public bool HasTrainingMetrics => BestMap > 0 || BestMap50 > 0;
}
