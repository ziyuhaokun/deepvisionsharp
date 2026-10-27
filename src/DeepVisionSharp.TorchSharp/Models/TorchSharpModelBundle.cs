using System.Text.Json;
using System.Text.Json.Nodes;
using DeepVisionSharp.Domain.ValueObjects;

namespace DeepVisionSharp.TorchSharp.Models;

/// <summary>
/// TorchSharp 模型元数据与配置捆绑包（支持 .pt.info / model-manifest.json / .torchscript）。
/// </summary>
public sealed class TorchSharpModelBundle
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public required string ModelFilePath { get; init; }

    public required string Task { get; init; }

    public required IReadOnlyList<string> Classes { get; init; }

    public int InputWidth { get; init; } = 640;

    public int InputHeight { get; init; } = 640;

    public int InputChannels { get; init; } = 3;

    public string? Arch { get; init; }

    /// <summary>Pixel normalisation the model was trained with; legacy sidecars mean unit scale.</summary>
    public PreprocessingVersion Preprocessing { get; init; } = PreprocessingVersion.UnitScaleV1;

    /// <summary>权重格式：null/默认按 TorchScript；<c>torchsharp</c> 表示 Module.save。</summary>
    public string? WeightsFormat { get; init; }

    public bool IsTorchSharpModuleWeights =>
        string.Equals(WeightsFormat, "torchsharp", StringComparison.OrdinalIgnoreCase)
        || ModelFilePath.EndsWith(".torchsharp", StringComparison.OrdinalIgnoreCase);

    public static TorchSharpModelBundle Load(string modelPath, string expectedTask)
    {
        var resolvedPath = ResolveModelPath(modelPath);
        if (!File.Exists(resolvedPath))
        {
            throw new FileNotFoundException($"模型文件不存在：{resolvedPath}", resolvedPath);
        }

        var dir = Path.GetDirectoryName(resolvedPath) ?? AppContext.BaseDirectory;

        // 1. 尝试读取同级 model-manifest.json
        var manifestPath = Path.Combine(dir, "model-manifest.json");
        if (File.Exists(manifestPath))
        {
            return Validate(LoadFromManifest(resolvedPath, manifestPath, expectedTask), manifestPath);
        }

        // 2. 尝试读取同名 .info (例如 model.pt.info / model.torchscript.info)
        var infoPath = resolvedPath + ".info";
        if (!File.Exists(infoPath) && resolvedPath.EndsWith(".torchscript", StringComparison.OrdinalIgnoreCase))
        {
            var basePtInfo = Path.ChangeExtension(resolvedPath, ".pt") + ".info";
            if (File.Exists(basePtInfo))
            {
                infoPath = basePtInfo;
            }
        }

        if (File.Exists(infoPath))
        {
            return Validate(LoadFromSidecarInfo(resolvedPath, infoPath, expectedTask), infoPath);
        }

        // 3. 无元数据：分类推理必须有类别与输入尺寸。Previously this fell back to
        // Classes=[] / 640x640, which made inference return "no result" instead of an error.
        throw new InvalidDataException(
            $"模型缺少元数据（model-manifest.json 或 {Path.GetFileName(resolvedPath)}.info），" +
            $"无法确定类别与输入尺寸：{resolvedPath}");
    }

    /// <summary>
    /// Rejects metadata that cannot drive classification. A sidecar without classes would
    /// silently produce zero detections, which is indistinguishable from "below threshold".
    /// </summary>
    private static TorchSharpModelBundle Validate(TorchSharpModelBundle bundle, string metadataPath)
    {
        if (bundle.Classes.Count < 2)
        {
            throw new InvalidDataException(
                $"模型元数据不完整：{Path.GetFileName(metadataPath)} 需要至少 2 个类别（classes），" +
                $"当前为 {bundle.Classes.Count}。");
        }

        if (bundle.InputWidth <= 0 || bundle.InputHeight <= 0)
        {
            throw new InvalidDataException(
                $"模型元数据不完整：{Path.GetFileName(metadataPath)} 的输入尺寸无效" +
                $"（{bundle.InputWidth}x{bundle.InputHeight}）。");
        }

        return bundle;
    }

    public static string ResolveModelPath(string modelPath)
    {
        if (string.IsNullOrWhiteSpace(modelPath))
        {
            throw new ArgumentException("模型路径不能为空", nameof(modelPath));
        }

        if (File.Exists(modelPath))
        {
            return Path.GetFullPath(modelPath);
        }

        if (Directory.Exists(modelPath))
        {
            // Models are saved with timestamped file names, so a folder usually holds several
            // checkpoints. Directory.GetFiles order is filesystem-defined; pick the newest instead,
            // which is the one a user pointing at the folder means.
            foreach (var pattern in new[] { "*.torchsharp", "*.torchscript", "*.pt" })
            {
                var newest = PickNewest(modelPath, pattern);
                if (newest is not null)
                {
                    return newest;
                }
            }
        }

        return Path.GetFullPath(modelPath);
    }

    private static string? PickNewest(string directory, string pattern)
    {
        try
        {
            return Directory.EnumerateFiles(directory, pattern)
                .Select(path => new FileInfo(path))
                .OrderByDescending(info => info.LastWriteTimeUtc)
                .ThenBy(info => info.Name, StringComparer.Ordinal)
                .Select(info => Path.GetFullPath(info.FullName))
                .FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static TorchSharpModelBundle LoadFromManifest(string modelFilePath, string manifestPath, string expectedTask)
    {
        var json = File.ReadAllText(manifestPath);
        var root = JsonNode.Parse(json) as JsonObject
            ?? throw new InvalidOperationException("model-manifest.json 格式错误");

        var task = root["task"]?.GetValue<string>()?.ToLowerInvariant() ?? expectedTask;
        var classesNode = root["classes"] as JsonArray;
        var classes = classesNode?.Select(c => c?.GetValue<string>() ?? "").Where(s => !string.IsNullOrWhiteSpace(s)).ToList()
            ?? new List<string>();

        var width = root["input_size"]?["width"]?.GetValue<int>() ?? 640;
        var height = root["input_size"]?["height"]?.GetValue<int>() ?? 640;
        var channels = root["input_channels"]?.GetValue<int>() ?? 3;
        var arch = root["arch"]?.GetValue<string>();

        return new TorchSharpModelBundle
        {
            ModelFilePath = modelFilePath,
            Task = task,
            Classes = classes,
            InputWidth = width,
            InputHeight = height,
            InputChannels = channels,
            Arch = arch,
            Preprocessing = PreprocessingVersions.Parse(root["preprocessing"]?.GetValue<string>())
        };
    }

    private static TorchSharpModelBundle LoadFromSidecarInfo(string modelFilePath, string infoPath, string expectedTask)
    {
        var json = File.ReadAllText(infoPath);
        var root = JsonNode.Parse(json) as JsonObject
            ?? throw new InvalidOperationException(".info 格式错误");

        var labelsNode = root["labels"] as JsonArray ?? root["classes"] as JsonArray;
        var classes = labelsNode?.Select(c => c?.GetValue<string>() ?? "").Where(s => !string.IsNullOrWhiteSpace(s)).ToList()
            ?? new List<string>();

        if (root["input_size"] is null && root["input_width"] is null && root["input_height"] is null)
        {
            throw new InvalidDataException(
                $"模型元数据不完整：{Path.GetFileName(infoPath)} 缺少 input_size / input_width。");
        }

        var inputSize = root["input_size"]?.GetValue<int>()
            ?? root["input_width"]?.GetValue<int>()
            ?? 640;
        var inputWidth = root["input_width"]?.GetValue<int>() ?? inputSize;
        var inputHeight = root["input_height"]?.GetValue<int>() ?? inputSize;
        var channels = root["input_channels"]?.GetValue<int>() ?? 3;
        var weightsFormat = root["weights_format"]?.GetValue<string>();
        var arch = root["arch"]?.GetValue<string>();

        return new TorchSharpModelBundle
        {
            ModelFilePath = modelFilePath,
            Task = expectedTask,
            Classes = classes,
            InputWidth = inputWidth,
            InputHeight = inputHeight,
            InputChannels = channels,
            Arch = arch,
            WeightsFormat = weightsFormat,
            Preprocessing = PreprocessingVersions.Parse(root["preprocessing"]?.GetValue<string>())
        };
    }
}
