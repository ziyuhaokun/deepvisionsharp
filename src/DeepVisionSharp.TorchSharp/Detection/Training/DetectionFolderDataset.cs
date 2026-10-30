using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeepVisionSharp.TorchSharp.Detection.Training;

public sealed record DetectionSample(string ImagePath, string LabelPath);

public sealed record DetectionDataset(
    IReadOnlyList<string> Classes,
    IReadOnlyList<DetectionSample> Train,
    IReadOnlyList<DetectionSample> Val,
    string Fingerprint);

/// <summary>
/// Reads the YOLO layout written by the detection exporter:
/// <c>dataset-manifest.json</c> + <c>{train,val}/{images,labels}</c>.
/// A missing label file is a valid negative sample.
/// </summary>
public static class DetectionFolderDataset
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff", ".webp" };

    public static DetectionDataset Load(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        var manifestPath = Path.Combine(root, "dataset-manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new InvalidDataException($"检测数据集缺少 dataset-manifest.json：{root}");
        }

        var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(manifestPath), Options)
            ?? throw new InvalidDataException($"dataset-manifest.json 无法解析：{manifestPath}");
        if (manifest.Classes is not { Count: > 0 })
        {
            throw new InvalidDataException("检测数据集清单缺少类别（classes）。");
        }

        var train = LoadSplit(root, "train");
        var val = LoadSplit(root, "val");
        if (train.Count == 0 || val.Count == 0)
        {
            throw new InvalidDataException("检测数据集的 train / val 至少各需要一张图像。");
        }

        return new DetectionDataset(
            manifest.Classes.Select(name => name.Trim()).Where(name => name.Length > 0).ToArray(),
            train,
            val,
            manifest.DatasetFingerprint ?? string.Empty);
    }

    private static List<DetectionSample> LoadSplit(string root, string split)
    {
        var imagesDirectory = Path.Combine(root, split, "images");
        var labelsDirectory = Path.Combine(root, split, "labels");
        if (!Directory.Exists(imagesDirectory))
        {
            throw new InvalidDataException($"检测数据集缺少 {split}/images 目录：{root}");
        }

        var samples = new List<DetectionSample>();
        foreach (var imagePath in Directory.EnumerateFiles(imagesDirectory, "*.*", SearchOption.AllDirectories))
        {
            if (!ImageExtensions.Contains(Path.GetExtension(imagePath)))
            {
                continue;
            }

            var relative = Path.GetRelativePath(imagesDirectory, imagePath);
            var labelPath = Path.Combine(labelsDirectory, Path.ChangeExtension(relative, ".txt"));
            samples.Add(new DetectionSample(imagePath, labelPath));
        }

        samples.Sort((left, right) => string.CompareOrdinal(left.ImagePath, right.ImagePath));
        return samples;
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private sealed class Manifest
    {
        [JsonPropertyName("classes")]
        public List<string>? Classes { get; set; }

        [JsonPropertyName("dataset_fingerprint")]
        public string? DatasetFingerprint { get; set; }
    }
}
