using System.Diagnostics;
using Microsoft.Extensions.Logging;
using DeepVisionSharp.Application.Inference;
using DeepVisionSharp.Application.Models;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Domain.ValueObjects;
using DeepVisionSharp.TorchSharp.Classification;
using DeepVisionSharp.TorchSharp.Device;

namespace DeepVisionSharp.Infrastructure.Services;

/// <summary>
/// 进程内 C# TorchSharp 分类推理后端（<see cref="TorchSharpClsEngine"/>）。
/// </summary>
internal sealed class TorchSharpClassificationInferenceBackend : IInferenceBackend
{
    private readonly TorchSharpClsEngine _engine;
    private readonly ILogger<TorchSharpClassificationInferenceBackend> _logger;

    public TorchSharpClassificationInferenceBackend(
        TorchSharpClsEngine engine,
        ILogger<TorchSharpClassificationInferenceBackend> logger)
    {
        _engine = engine;
        _logger = logger;
    }

    public string BackendKey => BackendKeys.InferenceTorchSharpClassification;

    public bool IsActive => false;

    public bool CanHandle(InferenceBackendContext context)
        => ModelTypeNames.IsClassificationModelType(context.ModelType)
           && LooksLikeTorchSharpClassificationWeights(context.ModelPath);

    public Task<InferenceWarmupResult> WarmupAsync(
        InferenceBackendContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(context.ModelPath) || !File.Exists(context.ModelPath))
        {
            return Task.FromResult(InferenceWarmupResult.Fail("TorchSharp 分类模型文件不存在。"));
        }

        try
        {
            var watch = Stopwatch.StartNew();
            var cached = _engine.Warmup(context.ModelPath, context.Config.Device);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(InferenceWarmupResult.Ok(0, watch.Elapsed.TotalMilliseconds,
                preflightCached: cached, loadSkipped: cached));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TorchSharp classification warmup failed");
            return Task.FromResult(InferenceWarmupResult.Fail(ex.Message));
        }
    }

    public Task<InferenceResult> RunSingleAsync(
        InferenceBackendContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(context.ImagePath) || !File.Exists(context.ImagePath))
            {
                return Task.FromResult(Failed("推理图像不存在"));
            }

            if (string.IsNullOrWhiteSpace(context.ModelPath) || !File.Exists(context.ModelPath))
            {
                return Task.FromResult(Failed("TorchSharp 分类模型文件不存在"));
            }

            var watch = Stopwatch.StartNew();
            var result = _engine.Infer(
                context.ModelPath,
                context.Config.Device,
                context.ImagePath,
                Math.Clamp(context.Config.ConfidenceThreshold, 0.0, 1.0));
            watch.Stop();

            var detections = result.Detection is not null ? new List<ClassificationResult> { result.Detection } : new List<ClassificationResult>();

            return Task.FromResult(new InferenceResult
            {
                Success = true,
                Detections = detections,
                ImageWidth = result.ImageWidth,
                ImageHeight = result.ImageHeight,
                Performance = new InferencePerformanceMetrics
                {
                    TotalMilliseconds = watch.Elapsed.TotalMilliseconds,
                    LoadSkipped = true,
                    ReadImageMilliseconds = result.ReadImageMilliseconds,
                    InferenceMilliseconds = result.InferenceMilliseconds,
                    PostprocessMilliseconds = 0,
                    DetectionCount = detections.Count,
                    TotalCrops = 1,
                    ImageWidth = result.ImageWidth,
                    ImageHeight = result.ImageHeight,
                    Device = result.Device,
                    CudaAvailable = false,
                    GpuDeviceName = result.Device
                }
            });
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult(Failed("TorchSharp 分类推理已取消"));
        }
        catch (InferenceBlockedByTrainingException ex)
        {
            // Expected while a training run holds the device; not an engine failure.
            return Task.FromResult(Failed(ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "C# TorchSharp classification inference failed");
            return Task.FromResult(Failed(ex.Message));
        }
    }

    public Task StopAsync(string? reason, CancellationToken cancellationToken) => Task.CompletedTask;

    private static bool LooksLikeTorchSharpClassificationWeights(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        if (path.EndsWith(".torchsharp", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".torchscript", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // .pt 仅在 sidecar 声明 weights_format=torchsharp 时接受，避免误吃 Ultralytics 权重
        if (!path.EndsWith(".pt", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var infoPath = path + ".info";
        if (!File.Exists(infoPath))
        {
            return false;
        }

        try
        {
            var json = File.ReadAllText(infoPath);
            return json.Contains("\"weights_format\"", StringComparison.OrdinalIgnoreCase)
                   && json.Contains("torchsharp", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static InferenceResult Failed(string error) =>
        new()
        {
            Success = false,
            Error = error
        };
}
