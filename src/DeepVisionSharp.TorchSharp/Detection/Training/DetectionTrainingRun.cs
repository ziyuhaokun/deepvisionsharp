using System.Text.Json;
using System.Text.Json.Serialization;
using DeepVisionSharp.TorchSharp.Detection.Yolo26;
using DeepVisionSharp.TorchSharp.IO;
using DeepVisionSharp.TorchSharp.Models;

namespace DeepVisionSharp.TorchSharp.Detection.Training;

/// <summary>Detection settings resolved by the trainer before its first checkpoint.</summary>
public sealed record DetectionTrainingRun(
    Guid? TrainingJobId,
    string ModelType,
    string Arch,
    string[] Classes,
    int InputWidth,
    int InputHeight,
    int InputChannels,
    string DatasetFingerprint,
    string Preprocessing,
    int[] Strides,
    [property: JsonRequired] bool SppfShortcut,
    float NmsIou,
    int MaxDetections,
    float EvaluationConfidence,
    float EvaluationScoreFloor,
    string? Pretrained,
    string? PretrainedArch,
    [property: JsonRequired] int PretrainedLoadedTensors,
    int ConfiguredEpochs,
    int BatchSize,
    float LearningRate,
    string Optimizer)
{
    public const string FileName = "detection-run.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    [JsonRequired]
    public int SchemaVersion { get; init; } = 1;

    public string Initialization => PretrainedLoadedTensors == 0 ? "random"
        : PretrainedArch!.EndsWith("-cls", StringComparison.OrdinalIgnoreCase) ? "classification_backbone" : "detection_pretrained";

    internal void Write(string outputRoot) =>
        AtomicFileWriter.WriteAllText(Path.Combine(outputRoot, FileName), JsonSerializer.Serialize(this, JsonOptions));

    public static DetectionTrainingRun Read(string outputRoot)
    {
        using var stream = File.OpenRead(Path.Combine(outputRoot, FileName));
        var run = JsonSerializer.Deserialize<DetectionTrainingRun>(stream, JsonOptions);
        if (run is null || run.SchemaVersion != 1
            || string.IsNullOrWhiteSpace(run.ModelType)
            || run.Arch != Yolo26DetectFactory.ResolveArch(run.ModelType)
            || run.Classes is not { Length: > 0 }
            || run.Classes.Any(string.IsNullOrWhiteSpace)
            || run.Classes.Distinct(StringComparer.OrdinalIgnoreCase).Count() != run.Classes.Length
            || run.InputWidth < 32 || run.InputWidth % 32 != 0 || run.InputHeight < 32 || run.InputHeight % 32 != 0
            || run.InputChannels is not (1 or 3)
            || string.IsNullOrWhiteSpace(run.DatasetFingerprint)
            || run.Preprocessing != PreprocessingVersions.LetterboxWire
            || run.Strides is not [Yolo26DetectModel.BaseStride, Yolo26DetectModel.BaseStride * 2, Yolo26DetectModel.BaseStride * 4]
            || run.NmsIou is <= 0 or > 1 || run.MaxDetections <= 0
            || run.PretrainedLoadedTensors < 0
            || (run.PretrainedLoadedTensors > 0 && (string.IsNullOrWhiteSpace(run.Pretrained) || string.IsNullOrWhiteSpace(run.PretrainedArch)))
            || (run.PretrainedLoadedTensors == 0 && (run.Pretrained is not null || run.PretrainedArch is not null)))
        {
            throw new InvalidDataException("检测训练记录缺少有效的模型、类别、输入、解码或初始化信息。");
        }

        return run;
    }

    public Dictionary<string, object?> CreateCheckpointMetadata() => new()
    {
        ["schema_version"] = 2,
        ["artifact_kind"] = "trained_model",
        ["task"] = "detect",
        ["model_type"] = ModelType,
        ["arch"] = Arch,
        ["classes"] = Classes,
        ["num_classes"] = Classes.Length,
        ["dataset_fingerprint"] = DatasetFingerprint,
        ["input_size"] = Math.Max(InputWidth, InputHeight),
        ["input_width"] = InputWidth,
        ["input_height"] = InputHeight,
        ["input_channels"] = InputChannels,
        ["preprocessing"] = Preprocessing,
        ["strides"] = Strides,
        ["sppf_shortcut"] = SppfShortcut,
        ["nms_iou"] = NmsIou,
        ["max_detections"] = MaxDetections,
        ["evaluation_confidence"] = EvaluationConfidence,
        ["evaluation_score_floor"] = EvaluationScoreFloor,
        ["configured_epochs"] = ConfiguredEpochs,
        ["batch_size"] = BatchSize,
        ["lr"] = LearningRate,
        ["optimizer"] = Optimizer,
        ["weights_format"] = TorchSharpDetTrainer.WeightsFormat,
        ["initialization"] = Initialization,
        ["pretrained"] = Pretrained,
        ["pretrained_arch"] = PretrainedArch,
        ["pretrained_loaded_tensors"] = PretrainedLoadedTensors,
        ["torchsharp"] = new Dictionary<string, object?>
        {
            ["arch"] = Arch,
            ["input_width"] = InputWidth,
            ["input_height"] = InputHeight,
            ["num_classes"] = Classes.Length,
            ["input_channels"] = InputChannels,
            ["stride"] = Strides[0]
        }
    };
}
