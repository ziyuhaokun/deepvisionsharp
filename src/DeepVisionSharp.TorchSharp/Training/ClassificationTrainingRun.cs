using System.Text.Json;
using System.Text.Json.Serialization;
using DeepVisionSharp.TorchSharp.Classification;
using DeepVisionSharp.TorchSharp.IO;
using DeepVisionSharp.TorchSharp.Models;

namespace DeepVisionSharp.TorchSharp.Training;

/// <summary>Classification settings resolved by the trainer before its first checkpoint.</summary>
public sealed record ClassificationTrainingRun(
    Guid? TrainingJobId,
    string ModelType,
    string Arch,
    string[] Classes,
    int InputWidth,
    int InputHeight,
    int InputChannels,
    string DatasetFingerprint,
    string Preprocessing,
    string? Pretrained,
    string? PretrainedArch,
    int PretrainedLoadedTensors,
    int ConfiguredEpochs,
    int BatchSize,
    float LearningRate,
    string Optimizer)
{
    public const string FileName = "classification-run.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    [JsonRequired]
    public int SchemaVersion { get; init; } = 1;

    internal void Write(string outputRoot) =>
        AtomicFileWriter.WriteAllText(Path.Combine(outputRoot, FileName), JsonSerializer.Serialize(this, JsonOptions));

    public static ClassificationTrainingRun Read(string outputRoot)
    {
        using var stream = File.OpenRead(Path.Combine(outputRoot, FileName));
        var run = JsonSerializer.Deserialize<ClassificationTrainingRun>(stream, JsonOptions);
        if (run is null || run.SchemaVersion != 1
            || string.IsNullOrWhiteSpace(run.ModelType)
            || run.Arch != TorchSharpClassifierFactory.ResolveArch(run.ModelType)
            || run.Classes is not { Length: >= 2 }
            || run.Classes.Any(string.IsNullOrWhiteSpace)
            || run.Classes.Distinct(StringComparer.OrdinalIgnoreCase).Count() != run.Classes.Length
            || run.InputWidth < 8 || run.InputHeight < 8 || run.InputChannels is not (1 or 3)
            || string.IsNullOrWhiteSpace(run.DatasetFingerprint)
            || run.Preprocessing is not (PreprocessingVersions.UnitScaleWire or PreprocessingVersions.ImageNetWire or PreprocessingVersions.LetterboxWire)
            || run.PretrainedLoadedTensors < 0
            || (run.PretrainedLoadedTensors > 0 && (string.IsNullOrWhiteSpace(run.Pretrained) || string.IsNullOrWhiteSpace(run.PretrainedArch)))
            || (run.PretrainedLoadedTensors == 0 && (run.Pretrained is not null || run.PretrainedArch is not null)))
        {
            throw new InvalidDataException("分类训练记录缺少有效的模型、类别、输入或初始化信息。");
        }

        return run;
    }

    public Dictionary<string, object?> CreateCheckpointMetadata() => new()
    {
        ["schema_version"] = 2,
        ["artifact_kind"] = "trained_model",
        ["task"] = "classify",
        ["training_job_id"] = TrainingJobId,
        ["model_type"] = ModelType,
        ["classes"] = Classes,
        ["num_classes"] = Classes.Length,
        ["max_detections"] = 1,
        ["dataset_fingerprint"] = DatasetFingerprint,
        ["configured_epochs"] = ConfiguredEpochs,
        ["batch_size"] = BatchSize,
        ["lr"] = LearningRate,
        ["optimizer"] = Optimizer,
        ["input_size"] = Math.Max(InputWidth, InputHeight),
        ["input_width"] = InputWidth,
        ["input_height"] = InputHeight,
        ["input_channels"] = InputChannels,
        ["weights_format"] = TorchSharpClsTrainer.WeightsFormat,
        ["preprocessing"] = Preprocessing,
        ["arch"] = Arch,
        ["initialization"] = PretrainedLoadedTensors == 0 ? "random" : "pretrained",
        ["pretrained"] = Pretrained,
        ["pretrained_arch"] = PretrainedArch,
        ["pretrained_loaded_tensors"] = PretrainedLoadedTensors,
        ["torchsharp"] = new Dictionary<string, object?>
        {
            ["arch"] = Arch,
            ["image_size"] = Math.Max(InputWidth, InputHeight),
            ["num_classes"] = Classes.Length,
            ["input_channels"] = InputChannels
        }
    };
}
