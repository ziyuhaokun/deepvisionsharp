using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TorchSharp;
using static TorchSharp.torch;

using DeepVisionSharp.Domain.ValueObjects;
using DeepVisionSharp.TorchSharp.Device;
using DeepVisionSharp.TorchSharp.Detection.Yolo26;
using DeepVisionSharp.TorchSharp.Models;
using DeepVisionSharp.TorchSharp.Tensors;
using DeepVisionSharp.TorchSharp.Training;

namespace DeepVisionSharp.TorchSharp.Detection;

/// <summary>
/// TorchSharp YOLO26 detection inference: letterbox → forward → decode/NMS → inverse letterbox.
/// Shares the slot cache, LRU eviction and training gate patterns with the classification engine.
/// </summary>
public sealed class TorchSharpDetectEngine : IDisposable
{
    private const int DefaultMaxCachedModels = 3;

    private readonly ILogger<TorchSharpDetectEngine>? _logger;
    private readonly ModelSlotCache<string, Slot> _cache;
    private readonly ReaderWriterLockSlim _lifetime = new();
    private readonly InferenceTrainingGate _gate;
    private bool _isDisposed;

    public TorchSharpDetectEngine(ILogger<TorchSharpDetectEngine>? logger = null, int maxCachedModels = DefaultMaxCachedModels)
    {
        _logger = logger;
        _cache = new ModelSlotCache<string, Slot>(maxCachedModels);
        _gate = new InferenceTrainingGate();
    }

    public InferenceTrainingGate Gate => _gate;

    public int LoadedModelCount => _cache.Count;

    public void ClearCache()
    {
        _lifetime.EnterReadLock();
        try
        {
            if (_isDisposed)
            {
                return;
            }

            _cache.Clear();
        }
        finally
        {
            _lifetime.ExitReadLock();
        }

        TorchDeviceMemory.EmptyCache(_logger);
    }

    public bool Warmup(string modelPath, string deviceName)
    {
        using var gateLease = _gate.EnterInference();
        using var operation = EnterOperation();
        var device = TorchSharpDeviceManager.ResolveDevice(deviceName);
        var key = BuildCacheKey(modelPath, device);
        var cached = _cache.Contains(key);
        _ = _cache.GetOrAdd(key, _ => CreateSlot(modelPath, device));
        return cached;
    }

    public TorchSharpDetInferResult Infer(
        string modelPath,
        string deviceName,
        string imagePath,
        double confidenceThreshold,
        double nmsIou,
        int maxDetections)
    {
        using var gateLease = _gate.EnterInference();
        using var operation = EnterOperation();

        var totalWatch = Stopwatch.StartNew();
        var targetDevice = TorchSharpDeviceManager.ResolveDevice(deviceName);
        using var slotLease = AcquireSlot(modelPath, targetDevice);
        var slot = slotLease.Value;

        var readWatch = Stopwatch.StartNew();
        using var source = TorchSharpImageTensor.LoadMat(imagePath, slot.Bundle.InputChannels);
        var width = source.Width;
        var height = source.Height;
        readWatch.Stop();

        var inferWatch = Stopwatch.StartNew();
        ScoredDetection[] decoded;
        var letterbox = DetectionLetterbox.For(width, height, slot.Bundle.InputWidth, slot.Bundle.InputHeight);
        lock (slot.InferLock)
        {
            using var scope = NewDisposeScope();
            using var noGrad = no_grad();
            using var canvas = letterbox.Apply(source);
            using var input = DetectionLetterbox.ToTensor(canvas, targetDevice, slot.Bundle.Preprocessing);
            var predictions = slot.Forward(input);
            try
            {
                decoded = DetectionTensorDecoder
                    .DecodeAll(
                        predictions,
                        slot.BaseStride,
                        (float)Math.Clamp(confidenceThreshold, 0.0, 1.0),
                        (float)Math.Clamp(nmsIou, 0.05, 0.95),
                        Math.Max(1, maxDetections))
                    .ToArray();
            }
            finally
            {
                foreach (var prediction in predictions)
                {
                    prediction.Dispose();
                }
            }
        }

        inferWatch.Stop();

        var boxes = new List<DetectionBoxResult>(decoded.Length);
        foreach (var detection in decoded)
        {
            var sourceBox = letterbox.ToSource(detection);
            if (sourceBox is null
                || sourceBox.Value.ClassIndex < 0
                || sourceBox.Value.ClassIndex >= slot.Bundle.Classes.Count)
            {
                continue;
            }

            var className = slot.Bundle.Classes[sourceBox.Value.ClassIndex];
            boxes.Add(new DetectionBoxResult
            {
                Label = className,
                ClassName = className,
                Confidence = sourceBox.Value.Score,
                X = sourceBox.Value.X,
                Y = sourceBox.Value.Y,
                Width = sourceBox.Value.Width,
                Height = sourceBox.Value.Height
            });
        }

        totalWatch.Stop();
        return new TorchSharpDetInferResult(
            boxes,
            width,
            height,
            targetDevice.ToString(),
            totalWatch.Elapsed.TotalMilliseconds,
            readWatch.Elapsed.TotalMilliseconds,
            inferWatch.Elapsed.TotalMilliseconds);
    }

    public void Dispose()
    {
        _lifetime.EnterWriteLock();
        try
        {
            if (_isDisposed) return;
            _isDisposed = true;
            _cache.Clear();
        }
        finally
        {
            _lifetime.ExitWriteLock();
        }

        TorchDeviceMemory.EmptyCache(_logger);
    }

    private IDisposable EnterOperation()
    {
        _lifetime.EnterReadLock();
        if (_isDisposed)
        {
            _lifetime.ExitReadLock();
            throw new ObjectDisposedException(nameof(TorchSharpDetectEngine));
        }

        return new ReadLease(_lifetime);
    }

    private sealed class ReadLease(ReaderWriterLockSlim lifetime) : IDisposable
    {
        public void Dispose() => lifetime.ExitReadLock();
    }

    private ModelSlotCache<string, Slot>.Lease AcquireSlot(string modelPath, torch.Device device)
        => _cache.Acquire(BuildCacheKey(modelPath, device), _ => CreateSlot(modelPath, device));

    private static string BuildCacheKey(string modelPath, torch.Device device)
        => $"{TorchSharpModelBundle.ResolveModelPath(modelPath)}|{device}";

    private Slot CreateSlot(string modelPath, torch.Device device)
    {
        var bundle = TorchSharpModelBundle.Load(modelPath, "detect");
        if (!bundle.IsTorchSharpModuleWeights)
        {
            throw new InvalidDataException("检测推理只支持本仓库训练的 .torchsharp 权重。");
        }

        _logger?.LogInformation(
            "TorchSharp Detect 加载模型 {Path} 至设备 {Device}",
            bundle.ModelFilePath,
            device);

        var module = Yolo26DetectFactory.Create(
            bundle.Arch,
            bundle.InputChannels,
            Math.Max(1, bundle.Classes.Count));
        try
        {
            module.load(bundle.ModelFilePath);
            module.to(device);
            module.eval();
            var baseStride = bundle.Strides.Count > 0 ? bundle.Strides[0] : Yolo26DetectModel.BaseStride;
            return new Slot { Bundle = bundle, ModuleModel = module, BaseStride = baseStride };
        }
        catch
        {
            module.Dispose();
            throw;
        }
    }

    private sealed class Slot : IDisposable
    {
        public required TorchSharpModelBundle Bundle { get; init; }
        public required Yolo26DetectModel ModuleModel { get; init; }
        public int BaseStride { get; init; } = Yolo26DetectModel.BaseStride;
        public object InferLock { get; } = new();

        public Tensor[] Forward(Tensor input) => ModuleModel.forward(input);

        public void Dispose() => ModuleModel.Dispose();
    }
}

public sealed record TorchSharpDetInferResult(
    IReadOnlyList<DetectionBoxResult> Boxes,
    int ImageWidth,
    int ImageHeight,
    string Device,
    double TotalMilliseconds,
    double ReadImageMilliseconds,
    double InferenceMilliseconds);
