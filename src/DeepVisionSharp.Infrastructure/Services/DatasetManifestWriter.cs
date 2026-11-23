using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DeepVisionSharp.Domain.Datasets;

namespace DeepVisionSharp.Infrastructure.Services;

/// <summary>Records the bytes handed to training and publishes the manifest after export completes.</summary>
internal sealed class DatasetManifestWriter(string outputRoot)
{
    private static readonly JsonSerializerOptions FingerprintOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly Dictionary<(Guid GalleryId, string FileName), ExportedImage> _images = new();
    private readonly List<DatasetManifestSample> _assignments = [];

    public async Task ExportImageAsync(
        Guid galleryId,
        string fileName,
        string split,
        string sourcePath,
        string targetPath,
        CancellationToken cancellationToken,
        int? classIndex = null,
        string? labelText = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var identity = (galleryId, fileName);
        if (_images.TryGetValue(identity, out var image))
        {
            // Link the second assignment to the first export. This also reuses the exact bytes
            // that were copied when the source gallery and the export are on different drives.
            FastFileLinker.TryLinkOrCopy(image.Path, targetPath, overwrite: true);
        }
        else
        {
            FastFileLinker.TryLinkOrCopy(sourcePath, targetPath, overwrite: true);
            await using var stream = new FileStream(targetPath, FileMode.Open, FileAccess.Read,
                FileShare.Read, bufferSize: 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
            image = new ExportedImage(targetPath, Convert.ToHexStringLower(hash));
            _images.Add(identity, image);
        }

        var labelHash = labelText is null ? null : Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(labelText)));
        var relativePath = Path.GetRelativePath(outputRoot, targetPath).Replace(Path.DirectorySeparatorChar, '/');
        _assignments.Add(new DatasetManifestSample(galleryId, fileName, split, relativePath,
            image.Sha256, classIndex, labelHash));
    }

    public async Task<DatasetManifest> WriteAsync(
        string task,
        string[] classes,
        string splitMethod,
        int? splitSeed,
        CancellationToken cancellationToken,
        int? trainingBoxes = null,
        int? validationBoxes = null)
    {
        var samples = _assignments
            .OrderBy(sample => sample.Split, StringComparer.Ordinal)
            .ThenBy(sample => sample.GalleryId)
            .ThenBy(sample => sample.FileName, StringComparer.Ordinal)
            .ToArray();
        var training = samples.Where(sample => sample.Split == "train")
            .Select(sample => (sample.GalleryId, sample.FileName)).ToHashSet();
        var validation = samples.Where(sample => sample.Split == "val")
            .Select(sample => (sample.GalleryId, sample.FileName)).ToHashSet();
        var fingerprint = await ComputeFingerprintAsync(task, classes, samples, cancellationToken).ConfigureAwait(false);
        var manifest = new DatasetManifest
        {
            SchemaVersion = DatasetManifest.CurrentVersion,
            Task = task,
            Classes = classes,
            DatasetFingerprint = fingerprint,
            TrainingCount = training.Count,
            ValidationCount = validation.Count,
            TrainingBoxes = trainingBoxes,
            ValidationBoxes = validationBoxes,
            SplitMethod = splitMethod,
            SplitSeed = splitSeed,
            SplitOverlapCount = training.Count(validation.Contains),
            Samples = samples
        };

        var path = Path.Combine(outputRoot, DatasetManifest.FileName);
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = File.Create(temporary))
            {
                await manifest.WriteAsync(stream, cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, path);
        }
        finally
        {
            File.Delete(temporary);
        }

        return manifest;
    }

    private static async Task<string> ComputeFingerprintAsync(
        string task, string[] classes, DatasetManifestSample[] samples, CancellationToken cancellationToken)
    {
        // Structured serialization preserves field boundaries and class order. The absolute
        // export path and the split settings themselves are outside the content fingerprint.
        using var sha = SHA256.Create();
        using var sink = new CryptoStream(Stream.Null, sha, CryptoStreamMode.Write);
        var payload = new { schema_version = DatasetManifest.CurrentVersion, task, classes, samples };
        await JsonSerializer.SerializeAsync(sink, payload, FingerprintOptions, cancellationToken).ConfigureAwait(false);
        await sink.FlushFinalBlockAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToHexStringLower(sha.Hash!);
    }

    private sealed record ExportedImage(string Path, string Sha256);
}
