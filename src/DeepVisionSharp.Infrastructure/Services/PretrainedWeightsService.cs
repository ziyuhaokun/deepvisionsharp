using DeepVisionSharp.Application.Models;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.TorchSharp.Classification;
using DeepVisionSharp.TorchSharp.Classification.Pretrained;
using DeepVisionSharp.TorchSharp.Models;
using Microsoft.Extensions.Logging;

namespace DeepVisionSharp.Infrastructure.Services;

/// <summary>
/// Reports and fetches ImageNet weights for the ResNet classifiers. Detection and YOLO26 models are
/// "unsupported" here: their checkpoints still need <c>scripts/fetch_pretrained.ps1</c>.
/// </summary>
internal sealed class PretrainedWeightsService : IPretrainedWeightsService
{
    private readonly PretrainedWeightsDownloader _downloader;
    private readonly ILogger<PretrainedWeightsService> _logger;

    public PretrainedWeightsService(ILogger<PretrainedWeightsService> logger)
    {
        _downloader = new PretrainedWeightsDownloader();
        _logger = logger;
    }

    public PretrainedWeightsStatus GetStatus(string modelType)
    {
        var source = ResolveSource(modelType, out var archName);
        if (source is null)
        {
            return new PretrainedWeightsStatus(false, false, string.Empty, 0);
        }

        var available = PretrainedWeightsLocator.Resolve(archName) is not null;
        return new PretrainedWeightsStatus(true, available, source.DisplayName, source.ApproxMegabytes);
    }

    public async Task DownloadAsync(string modelType, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var source = ResolveSource(modelType, out _)
            ?? throw new InvalidOperationException($"模型 {modelType} 不支持在软件里下载预训练权重。");

        _logger.LogInformation("Downloading pretrained weights {Arch} from {Url}", source.Arch, source.Url);
        var target = await _downloader.DownloadAsync(source, progress, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Pretrained weights {Arch} installed at {Path}", source.Arch, target);
    }

    private static PretrainedWeightsSource? ResolveSource(string modelType, out string archName)
    {
        archName = string.Empty;
        // ResolveArch falls back to ResNet-18 for unknown keys, so gate on the family first.
        if (!ModelTypeNames.IsClassificationModelType(modelType))
        {
            return null;
        }

        archName = TorchSharpClassifierFactory.ResolveArch(modelType);
        return PretrainedWeightsCatalog.ForArch(archName);
    }
}
