using System.IO;
using System.Text.Json;
using DeepVisionSharp.Domain.ValueObjects;

namespace DeepVisionSharp.Presentation.Services;

public sealed record TrainingPreviewItem(
    string ImagePath,
    string Title,
    string? GroundTruth = null,
    string? Prediction = null,
    float? Confidence = null)
{
    public bool HasPrediction => Confidence.HasValue;

    public bool PredictionMatchesLabel => HasPrediction && string.Equals(GroundTruth, Prediction, StringComparison.Ordinal);

    public string PredictionText => $"{Prediction} · {Confidence:P0}";
}

public sealed class TrainingPreviewService
{
    public IReadOnlyList<TrainingPreviewItem> GetPreviewImages(string? outputRoot)
    {
        if (string.IsNullOrWhiteSpace(outputRoot))
        {
            return [];
        }

        var previewDirectory = Path.Combine(outputRoot, "preview");
        if (Directory.Exists(previewDirectory))
        {
            var sampleDirectories = new DirectoryInfo(previewDirectory)
                .EnumerateDirectories($"{TrainingPreviewManifest.SampleDirectoryPrefix}*")
                .Where(directory => TrainingPreviewManifest.IsSampleDirectoryName(directory.Name))
                .OrderByDescending(directory => directory.CreationTimeUtc);
            foreach (var directory in sampleDirectories)
            {
                var samples = ReadClassificationSamples(directory.FullName);
                if (samples.Count > 0)
                {
                    return samples;
                }
            }

            var legacy = Directory.EnumerateFiles(previewDirectory, "preview_*.jpg")
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(path => new TrainingPreviewItem(path, Path.GetFileName(path)))
                .ToArray();
            if (legacy.Length > 0)
            {
                return legacy;
            }
        }

        var detectionDirectory = Path.Combine(outputRoot, "previews");
        return Directory.Exists(detectionDirectory)
            ? Directory.EnumerateFiles(detectionDirectory, "epoch_*.png")
                .Select(path => (Path: path, Epoch: ParseEpoch(path)))
                .Where(image => image.Epoch > 0)
                .OrderByDescending(image => image.Epoch)
                .Take(9)
                .Select(image => new TrainingPreviewItem(image.Path, $"第 {image.Epoch} 轮"))
                .ToArray()
            : [];
    }

    private static IReadOnlyList<TrainingPreviewItem> ReadClassificationSamples(string directory)
    {
        var manifestPath = Path.Combine(directory, TrainingPreviewManifest.FileName);
        if (!File.Exists(manifestPath))
        {
            return [];
        }

        try
        {
            using var stream = new FileStream(manifestPath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            var manifest = JsonSerializer.Deserialize<TrainingPreviewManifest>(stream);
            if (manifest?.Samples is null)
            {
                return [];
            }

            return manifest.Samples
                .Where(sample => sample is not null && IsImageFileName(sample.ImageFileName))
                .Select(sample => new TrainingPreviewItem(
                    Path.Combine(directory, sample.ImageFileName), sample.SampleName,
                    sample.GroundTruth, sample.Prediction, sample.Confidence))
                .Where(image => File.Exists(image.ImagePath))
                .ToArray();
        }
        catch (IOException)
        {
            return [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static bool IsImageFileName(string? name) =>
        name is not null
        && Path.GetFileName(name) == name
        && name.StartsWith("sample_", StringComparison.Ordinal)
        && name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase);

    private static int ParseEpoch(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        return int.TryParse(name.AsSpan("epoch_".Length), out var epoch) ? epoch : 0;
    }
}
