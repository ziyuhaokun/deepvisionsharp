using System.Net.Http;
using System.Security.Cryptography;
using DeepVisionSharp.TorchSharp.Models;

namespace DeepVisionSharp.TorchSharp.Classification.Pretrained;

/// <summary>One downloadable ImageNet checkpoint.</summary>
/// <param name="Arch">Bundle directory name, as <see cref="PretrainedWeightsLocator"/> looks it up.</param>
/// <param name="Url">Official torchvision download address.</param>
/// <param name="Sha256Prefix">torchvision names files <c>name-&lt;first 8 hex of sha256&gt;.pth</c>.</param>
/// <param name="ApproxMegabytes">Download size, for the UI only.</param>
public sealed record PretrainedWeightsSource(string Arch, string DisplayName, string Url, string Sha256Prefix, int ApproxMegabytes);

/// <summary>Which classification architectures can fetch ImageNet weights, and from where.</summary>
public static class PretrainedWeightsCatalog
{
    private static readonly PretrainedWeightsSource ResNet18 = new(
        "resnet18", "ResNet-18", "https://download.pytorch.org/models/resnet18-f37072fd.pth", "f37072fd", 45);

    private static readonly PretrainedWeightsSource ResNet34 = new(
        "resnet34", "ResNet-34", "https://download.pytorch.org/models/resnet34-b627a593.pth", "b627a593", 84);

    public static IReadOnlyList<PretrainedWeightsSource> All { get; } = [ResNet18, ResNet34];

    /// <summary>
    /// Source for an internal architecture name (<c>resnet34_v2</c> and friends). ResNet-Tiny is sliced
    /// from the ResNet-18 bundle. YOLO26 is not downloadable here (its checkpoints need the Python converter).
    /// </summary>
    public static PretrainedWeightsSource? ForArch(string? archName)
    {
        var normalized = archName?.Trim().ToLowerInvariant();
        return normalized switch
        {
            "resnet18_v2" or "resnet18" or "resnet_tiny_v2" or "resnet-tiny" or "resnettiny" => ResNet18,
            "resnet34_v2" or "resnet34" => ResNet34,
            _ => null,
        };
    }
}

/// <summary>
/// Downloads an official ImageNet checkpoint, checks it against the hash torchvision puts in the file
/// name, converts it to a bundle in pure C#, and moves it into place. No Python involved.
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

    public PretrainedWeightsDownloader(HttpClient http, Func<string> root)
    {
        _http = http;
        _root = root;
    }

    /// <summary>Returns the final bundle directory. Progress is 0–1 over download plus conversion.</summary>
    public async Task<string> DownloadAsync(
        PretrainedWeightsSource source,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        var root = _root();
        Directory.CreateDirectory(root);
        // Work next to the destination so the final move is a rename on the same volume.
        var work = Path.Combine(root, $".download-{Guid.NewGuid():N}");
        Directory.CreateDirectory(work);
        try
        {
            var checkpoint = Path.Combine(work, "source.pth");
            await DownloadFileAsync(source, checkpoint, progress, cancellationToken).ConfigureAwait(false);

            progress?.Report(0.9);
            var bundleDirectory = Path.Combine(work, "bundle");
            await Task.Run(
                () =>
                {
                    var tensors = TorchCheckpointReader.ReadStateDict(checkpoint);
                    PretrainedBundleWriter.Write(tensors, bundleDirectory, source.Arch);
                },
                cancellationToken).ConfigureAwait(false);

            var target = Path.Combine(root, source.Arch);
            if (Directory.Exists(target))
            {
                Directory.Delete(target, recursive: true);
            }

            Directory.Move(bundleDirectory, target);
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
            int read;
            while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                sha.TransformBlock(buffer, 0, read, null, 0);
                received += read;
                if (total is > 0)
                {
                    progress?.Report(0.9 * received / total.Value);
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
