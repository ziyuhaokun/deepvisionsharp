using DeepVisionSharp.Application.Models;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.TorchSharp.Classification;
using DeepVisionSharp.TorchSharp.Classification.Pretrained;
using DeepVisionSharp.TorchSharp.Detection.Yolo26;
using DeepVisionSharp.TorchSharp.Models;
using Microsoft.Extensions.Logging;

namespace DeepVisionSharp.Infrastructure.Services;

/// <summary>
/// Reports and fetches pretrained weights for the ResNet and YOLO26 models, entirely in C#.
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

        var directory = PretrainedWeightsLocator.Resolve(archName);
        var usesFallback = ModelTypeNames.IsDetectionModelType(modelType)
            && directory is not null
            && !string.Equals(Path.GetFileName(directory), source.Arch, StringComparison.OrdinalIgnoreCase);
        return new PretrainedWeightsStatus(true, directory is not null, source.DisplayName, source.ApproxMegabytes, usesFallback);
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
        // The two factories fall back to a default for unknown keys, so gate on the family first.
        if (ModelTypeNames.IsClassificationModelType(modelType))
        {
            archName = TorchSharpClassifierFactory.ResolveArch(modelType);
        }
        else if (ModelTypeNames.IsDetectionModelType(modelType))
        {
            archName = Yolo26DetectFactory.ResolveArch(modelType);
        }
        else
        {
            return null;
        }

        return PretrainedWeightsCatalog.ForArch(archName);
    }
}
