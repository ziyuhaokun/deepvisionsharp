using System.Diagnostics;

using Microsoft.Extensions.Logging;

using DeepVisionSharp.Application.Inference;
using DeepVisionSharp.Application.Models;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Domain.ValueObjects;
using DeepVisionSharp.TorchSharp.Detection;
using DeepVisionSharp.TorchSharp.Device;

namespace DeepVisionSharp.Infrastructure.Services;

/// <summary>进程内 TorchSharp 检测推理后端（<see cref="TorchSharpDetectEngine"/>）。</summary>
internal sealed class TorchSharpDetectionInferenceBackend : IInferenceBackend
{
    private readonly TorchSharpDetectEngine _engine;
    private readonly ILogger<TorchSharpDetectionInferenceBackend> _logger;

    public TorchSharpDetectionInferenceBackend(
        TorchSharpDetectEngine engine,
        ILogger<TorchSharpDetectionInferenceBackend> logger)
    {
        _engine = engine;
        _logger = logger;
    }

    public string BackendKey => BackendKeys.InferenceTorchSharpDetection;

    public bool CanHandle(InferenceBackendContext context)
        => ModelTypeNames.IsDetectionModelType(context.ModelType)
           && LooksLikeTorchSharpWeights(context.ModelPath);

    public Task<InferenceWarmupResult> WarmupAsync(
        InferenceBackendContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(context.ModelPath) || !File.Exists(context.ModelPath))
        {
            return Task.FromResult(InferenceWarmupResult.Fail("TorchSharp 检测模型文件不存在。"));
        }

        try
        {
            var watch = Stopwatch.StartNew();
            var cached = _engine.Warmup(context.ModelPath, context.Config.Device);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(InferenceWarmupResult.Ok(0, watch.Elapsed.TotalMilliseconds,
                preflightCached: cached, loadSkipped: cached));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TorchSharp detection warmup failed");
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
                return Task.FromResult(Failed("TorchSharp 检测模型文件不存在"));
            }

            var watch = Stopwatch.StartNew();
            var result = _engine.Infer(
                context.ModelPath,
                context.Config.Device,
                context.ImagePath,
                Math.Clamp(context.Config.ConfidenceThreshold, 0.0, 1.0),
                Math.Clamp(context.Config.NmsIouThreshold, 0.05, 0.95),
                context.Config.MaxDetections);
            watch.Stop();

            var boxes = result.Boxes.ToList();
            return Task.FromResult(new InferenceResult
            {
                Success = true,
                Boxes = boxes,
                ImageWidth = result.ImageWidth,
                ImageHeight = result.ImageHeight,
                Performance = new InferencePerformanceMetrics
                {
                    TotalMilliseconds = watch.Elapsed.TotalMilliseconds,
                    LoadSkipped = true,
                    ReadImageMilliseconds = result.ReadImageMilliseconds,
                    InferenceMilliseconds = result.InferenceMilliseconds,
                    ResultCount = boxes.Count,
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
            return Task.FromResult(Failed("TorchSharp 检测推理已取消"));
        }
        catch (InferenceBlockedByTrainingException ex)
        {
            return Task.FromResult(Failed(ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "C# TorchSharp detection inference failed");
            return Task.FromResult(Failed(ex.Message));
        }
    }

    private static bool LooksLikeTorchSharpWeights(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        if (path.EndsWith(".torchsharp", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

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
