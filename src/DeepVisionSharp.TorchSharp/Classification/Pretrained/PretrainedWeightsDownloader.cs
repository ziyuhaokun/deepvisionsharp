using System.Net.Http;
using System.Security.Cryptography;
using DeepVisionSharp.TorchSharp.Models;

namespace DeepVisionSharp.TorchSharp.Classification.Pretrained;

/// <summary>How a downloaded checkpoint is laid out.</summary>
public enum PretrainedCheckpointFormat
{
    /// <summary>A plain state dict (torchvision <c>.pth</c>).</summary>
    StateDict,

    /// <summary>An Ultralytics training result (<c>.pt</c>) holding a pickled module tree.</summary>
    UltralyticsCheckpoint,
}

/// <summary>One downloadable pretrained checkpoint.</summary>
/// <param name="Arch">Bundle directory name, as <see cref="PretrainedWeightsLocator"/> looks it up.</param>
/// <param name="Url">Official download address.</param>
/// <param name="Sha256Prefix">Expected SHA-256, in full or as a prefix (torchvision only publishes the first 8 hex).</param>
/// <param name="ApproxMegabytes">Download size, for the UI only.</param>
public sealed record PretrainedWeightsSource(
    string Arch,
    string DisplayName,
    string Url,
    string Sha256Prefix,
    int ApproxMegabytes,
    PretrainedCheckpointFormat Format = PretrainedCheckpointFormat.StateDict);

/// <summary>Which architectures can fetch pretrained weights, and from where.</summary>
public static class PretrainedWeightsCatalog
{
    private const string UltralyticsRelease = "https://github.com/ultralytics/assets/releases/download/v8.4.0";

    private static readonly PretrainedWeightsSource ResNet18 = new(
        "resnet18", "ResNet-18", "https://download.pytorch.org/models/resnet18-f37072fd.pth", "f37072fd", 45);

    private static readonly PretrainedWeightsSource ResNet34 = new(
        "resnet34", "ResNet-34", "https://download.pytorch.org/models/resnet34-b627a593.pth", "b627a593", 84);

    // Pinned to the v8.4.0 release assets; the hashes were taken from those exact files, so a changed
    // upload fails the check instead of silently installing different weights.
    private static readonly Dictionary<string, PretrainedWeightsSource> Yolo = BuildYolo();

    public static IReadOnlyList<PretrainedWeightsSource> All { get; } = [ResNet18, ResNet34, .. Yolo.Values];

    /// <summary>
    /// Source for an internal architecture name (<c>resnet34_v2</c>, <c>yolo26n-cls</c>, <c>yolo26-n-det</c> and friends).
    /// ResNet-Tiny is sliced from the ResNet-18 bundle. A YOLO26 detector starts from the COCO detection
    /// checkpoint (bundle <c>yolo26n</c>), a YOLO26 classifier from the ImageNet one (<c>yolo26n-cls</c>).
    /// </summary>
    public static PretrainedWeightsSource? ForArch(string? archName)
    {
        var normalized = archName?.Trim().ToLowerInvariant();
        switch (normalized)
        {
            case "resnet18_v2" or "resnet18" or "resnet_tiny_v2" or "resnet-tiny" or "resnettiny":
                return ResNet18;
            case "resnet34_v2" or "resnet34":
                return ResNet34;
        }

        if (normalized is not null
            && PretrainedWeightsLocator.TryParseYolo26(normalized, out var scale, out var classifier)
            && Yolo.TryGetValue(BundleName(scale, classifier), out var source))
        {
            return source;
        }

        return null;
    }

    private static string BundleName(char scale, bool classifier) => $"yolo26{scale}{(classifier ? "-cls" : string.Empty)}";

    private static Dictionary<string, PretrainedWeightsSource> BuildYolo()
    {
        // scale, detection (hash, MB), classification (hash, MB)
        (char Scale, string DetSha, int DetMb, string ClsSha, int ClsMb)[] table =
        [
            ('n', "9b09cc8bf347f0fc8a5f7657480587f25db09b34bf33b0652110fb03a8ad4fef", 6, "0dd6f8dbc448870ac98a3cbb7156f923f7ce21fed3755d4019169ffffd279e81", 6),
            ('s', "646f8bc3fe0a656803d95c294f7852321748cb29d13466a1af8862e2db384a1b", 20, "816790029d5df3fef358f03c8144b96339d8824ee25577aeda8be0963e5c5f09", 14),
            ('m', "401cea9ab23ad19246ff7744859816bc599f350e93c9dd30367b6f0a0745d0b7", 44, "9f6546f33a70d910e2cd6dcca5265c4617b5670b19a1f287c39e99258bade01a", 24),
            ('l', "9fe3c544f2b19bebad7ea41e76d7ad3d88b7c2f10d11d24430c5311f6b32db26", 53, "7a3aebb7de6f5d79f0153da627a5f68bd3d7df30e648c0fdb7f9217986ee129e", 29),
            ('x', "9fdd44a31c504547ffb81d2c6d9e6dac3493c8eaa8b0398d3f43bae6c7003e92", 119, "ee88a0c71e9596cdfdbb71892725929cb4489b874d24fb581d20bffe79724386", 60),
        ];

        var result = new Dictionary<string, PretrainedWeightsSource>(StringComparer.Ordinal);
        foreach (var row in table)
        {
            var det = BundleName(row.Scale, classifier: false);
            result[det] = new PretrainedWeightsSource(
                det, $"YOLO26-{char.ToUpperInvariant(row.Scale)} 检测", $"{UltralyticsRelease}/{det}.pt", row.DetSha, row.DetMb,
                PretrainedCheckpointFormat.UltralyticsCheckpoint);

            var cls = BundleName(row.Scale, classifier: true);
            result[cls] = new PretrainedWeightsSource(
                cls, $"YOLO26-{char.ToUpperInvariant(row.Scale)}", $"{UltralyticsRelease}/{cls}.pt", row.ClsSha, row.ClsMb,
                PretrainedCheckpointFormat.UltralyticsCheckpoint);
        }

        return result;
    }
}

/// <summary>
/// Downloads an official checkpoint, verifies its SHA-256, converts it to a bundle in pure C#, and
/// moves it into place. No Python involved.
/// </summary>
public sealed class PretrainedWeightsDownloader
{
    private static readonly HttpClient SharedClient = CreateClient();

    private readonly HttpClient _http;
    private readonly Func<string> _root;

    public PretrainedWeightsDownloader()
        : this(SharedClient, () => PretrainedWeightsLocator.RootDirectory)
    {
    }

    public PretrainedWeightsDownloader(string root)
        : this(SharedClient, () => root)
    {
    }

    public PretrainedWeightsDownloader(HttpClient http, Func<string> root)
    {
        _http = http;
        _root = root;
    }

    /// <summary>Returns the final bundle directory. Progress is 0–1 over download plus conversion.</summary>
    public async Task<string> DownloadAsync(
        PretrainedWeightsSource source,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default,
        string? sourcePath = null,
        bool keepSource = false)
    {
        ArgumentNullException.ThrowIfNull(source);

        var root = _root();
        Directory.CreateDirectory(root);
        // Work next to the destination so the final move is a rename on the same volume.
        var work = Path.Combine(root, $".download-{Guid.NewGuid():N}");
        Directory.CreateDirectory(work);
        try
        {
            var extension = source.Format == PretrainedCheckpointFormat.UltralyticsCheckpoint ? ".pt" : ".pth";
            var checkpoint = sourcePath ?? Path.Combine(work, "source" + extension);
            if (sourcePath is null)
            {
                await DownloadFileAsync(source, checkpoint, progress, cancellationToken).ConfigureAwait(false);
            }

            progress?.Report(0.9);
            var bundleDirectory = Path.Combine(work, "bundle");
            await Task.Run(
                () =>
                {
                    var tensors = source.Format == PretrainedCheckpointFormat.UltralyticsCheckpoint
                        ? TorchCheckpointReader.ReadModuleStateDict(checkpoint)
                        : TorchCheckpointReader.ReadStateDict(checkpoint);
                    PretrainedBundleWriter.Write(tensors, bundleDirectory, source.Arch);
                },
                cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            var target = Path.Combine(root, source.Arch);
            try
            {
                Directory.Move(bundleDirectory, target);
            }
            catch (IOException) when (File.Exists(Path.Combine(target, "index.json")))
            {
                // A simultaneous install or an existing bundle wins. Leave its contents intact.
            }

            if (keepSource && sourcePath is null)
            {
                File.Move(checkpoint, Path.Combine(root, $"{source.Arch}-{Guid.NewGuid():N}{extension}"));
            }

            progress?.Report(1);
            return target;
        }
        finally
        {
            try
            {
                Directory.Delete(work, recursive: true);
            }
            catch
            {
                // A leftover scratch folder must not turn a finished download into a failure.
            }
        }
    }

    private async Task DownloadFileAsync(
        PretrainedWeightsSource source,
        string destination,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        using var response = await _http
            .GetAsync(source.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength;
        using var sha = SHA256.Create();
        await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        await using (var output = File.Create(destination))
        {
            var buffer = new byte[81920];
            long received = 0;
            var lastPercent = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                sha.TransformBlock(buffer, 0, read, null, 0);
                received += read;
                if (total is > 0)
                {
                    var percent = Math.Min(89, (int)(90d * received / total.Value));
                    if (percent > lastPercent)
                    {
                        lastPercent = percent;
                        progress?.Report(percent / 100d);
                    }
                }
            }
        }

        sha.TransformFinalBlock([], 0, 0);
        var actual = Convert.ToHexString(sha.Hash!).ToLowerInvariant();
        if (!actual.StartsWith(source.Sha256Prefix, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"下载的 {source.DisplayName} 权重校验失败（期望 {source.Sha256Prefix}…，实际 {actual[..8]}…），请重试。");
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DeepVisionSharp");
        return client;
    }
}
