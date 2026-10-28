using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Logging;
using TorchSharp;
using TorchSharp.Modules;
using DeepVisionSharp.Domain.ValueObjects;
using DeepVisionSharp.TorchSharp.Device;
using DeepVisionSharp.TorchSharp.Models;
using DeepVisionSharp.TorchSharp.Tensors;
using DeepVisionSharp.TorchSharp.Training;
using static TorchSharp.torch;
using static TorchSharp.torch.nn;

namespace DeepVisionSharp.TorchSharp.Classification;

/// <summary>
/// TorchSharp 分类推理：支持 TorchScript（.torchscript）与本仓库训练产物（.torchsharp / Module.save）。
/// </summary>
public sealed class TorchSharpClsEngine : IDisposable
{
    private const int DefaultMaxCachedModels = 3;

    private readonly ILogger<TorchSharpClsEngine>? _logger;
    private readonly ModelSlotCache<string, Slot> _cache;
    private readonly ReaderWriterLockSlim _lifetime = new();
    private readonly InferenceTrainingGate _gate;
    private bool _isDisposed;

    public TorchSharpClsEngine(ILogger<TorchSharpClsEngine>? logger = null, int maxCachedModels = DefaultMaxCachedModels)
    {
        _logger = logger;
        _cache = new ModelSlotCache<string, Slot>(maxCachedModels);
        _gate = new InferenceTrainingGate();
    }

    /// <summary>
    /// Serialises training against inference on the shared device. Training holds the gate for the
    /// whole run; inference fails fast while it is held.
    /// </summary>
    public InferenceTrainingGate Gate => _gate;

    /// <summary>Number of native models currently resident (eviction is LRU + lease aware).</summary>
    public int LoadedModelCount => _cache.Count;

    /// <summary>
    /// Releases cached native models without disabling the engine. Used before training so the
    /// trainer is not competing with resident inference models for GPU memory.
    /// </summary>
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

        // The cache is empty now, but PyTorch's allocator still holds the freed blocks. Returning
        // them to the driver is what lets a following training run claim the memory.
        TorchDeviceMemory.EmptyCache(_logger);
    }

    public TorchSharpClsInferResult Infer(
        string modelPath,
        string deviceName,
        string imagePath,
        double confidenceThreshold)
    {
        // Registered before EnterOperation so training's drain wait observes this call.
        using var gateLease = _gate.EnterInference();
        using var operation = EnterOperation();

        var totalWatch = Stopwatch.StartNew();
        var targetDevice = TorchSharpDeviceManager.ResolveDevice(deviceName);
        using var slotLease = AcquireSlot(modelPath, targetDevice);
        var slot = slotLease.Value;

        var readWatch = Stopwatch.StartNew();
        using var mat = TorchSharpImageTensor.LoadMat(imagePath, slot.Bundle.InputChannels);
        var origWidth = mat.Width;
        var origHeight = mat.Height;
        readWatch.Stop();

        int topClass;
        float score;

        var inferWatch = Stopwatch.StartNew();
        lock (slot.InferLock)
        {
            using var scope = NewDisposeScope();
            using var noGrad = no_grad();

            using var inputTensor = TorchSharpImageTensor.ToClassificationTensor(
                mat,
                slot.Bundle.InputWidth,
                slot.Bundle.InputHeight,
                targetDevice,
                preprocessing: slot.Bundle.Preprocessing);

            using var outputTensor = slot.Forward(inputTensor);
            using var cpuTensor = outputTensor.to(CPU);
            var logits = cpuTensor.flatten().data<float>().ToArray();

            DecodeTopClass(logits, slot.Bundle.Classes.Count, out topClass, out score);
        }
        inferWatch.Stop();

        ClassificationResult? detection = null;
        if (score >= (float)confidenceThreshold && topClass >= 0 && topClass < slot.Bundle.Classes.Count)
        {
            var className = slot.Bundle.Classes[topClass];
            detection = new ClassificationResult
            {
                Label = className,
                ClassName = className,
                Confidence = score
            };
        }

        totalWatch.Stop();
        return new TorchSharpClsInferResult(
            detection,
            origWidth,
            origHeight,
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
        finally { _lifetime.ExitWriteLock(); }

        TorchDeviceMemory.EmptyCache(_logger);
    }

    /// <summary>Load and validate the actual model using the same cache and lifetime as inference.</summary>
    public bool Warmup(string modelPath, string deviceName)
    {
        // Warmup loads native weights and consumes the same device memory as inference. It must
        // participate in the same gate so training cannot start while a model is still loading.
        using var gateLease = _gate.EnterInference();
        using var operation = EnterOperation();
        var device = TorchSharpDeviceManager.ResolveDevice(deviceName);
        var key = BuildCacheKey(modelPath, device);
        var cached = _cache.Contains(key);
        _ = _cache.GetOrAdd(key, _ => CreateSlot(modelPath, device));
        return cached;
    }

    private IDisposable EnterOperation()
    {
        _lifetime.EnterReadLock();
        if (_isDisposed)
        {
            _lifetime.ExitReadLock();
            throw new ObjectDisposedException(nameof(TorchSharpClsEngine));
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
        var bundle = TorchSharpModelBundle.Load(modelPath, "classify");
        _logger?.LogInformation(
            "TorchSharp Classify 加载模型 {Path}（format={Format}）至设备 {Device}",
            bundle.ModelFilePath,
            bundle.IsTorchSharpModuleWeights ? "torchsharp" : "torchscript",
            device);

        if (bundle.IsTorchSharpModuleWeights)
        {
            var imageSize = Math.Max(bundle.InputWidth, bundle.InputHeight);
            var archName = bundle.Arch;
            var module = TorchSharpClassifierFactory.Create(
                archName,
                bundle.InputChannels,
                Math.Max(2, bundle.Classes.Count),
                Math.Max(8, imageSize));
            try
            {
                module.load(bundle.ModelFilePath);
                module.to(device);
                module.eval();
                return new Slot { Bundle = bundle, ModuleModel = module };
            }
            catch { module.Dispose(); throw; }
        }

        var scripted = torch.jit.load(bundle.ModelFilePath, device);
        try
        {
            scripted.eval();
            return new Slot { Bundle = bundle, ScriptModel = scripted };
        }
        catch { scripted.Dispose(); throw; }
    }

    private static void DecodeTopClass(float[] logits, int classCount, out int topClass, out float confidence)
    {
        if (logits.Length == 0)
        {
            topClass = -1;
            confidence = 0f;
            return;
        }

        // The output width must match the sidecar class list. A narrower head would silently shift
        // class names; a wider one would drop the real winner and renormalise over the wrong logits,
        // producing a confidently wrong label. Refuse to decode instead of guessing.
        if (classCount <= 0 || logits.Length != classCount)
        {
            throw new InvalidDataException(
                $"模型输出维度与元数据类别数不一致：logits={logits.Length}, classes={classCount}");
        }

        var count = classCount;
        var maxVal = logits[0];
        var maxIdx = 0;

        for (var i = 1; i < count; i++)
        {
            if (logits[i] > maxVal)
            {
                maxVal = logits[i];
                maxIdx = i;
            }
        }

        topClass = maxIdx;

        double sumExp = 0;
        for (var i = 0; i < count; i++)
        {
            sumExp += Math.Exp(logits[i] - maxVal);
        }

        // top 类相对 max 的 softmax 概率为 1/sumExp
        confidence = sumExp > 0 ? (float)(1.0 / sumExp) : 1.0f;
    }

    private sealed class Slot : IDisposable
    {
        public required TorchSharpModelBundle Bundle { get; init; }
        public torch.jit.ScriptModule? ScriptModel { get; init; }
        public Module<Tensor, Tensor>? ModuleModel { get; init; }
        public object InferLock { get; } = new();

        public Tensor Forward(Tensor input)
        {
            if (ModuleModel is not null)
            {
                return ModuleModel.forward(input);
            }

            if (ScriptModel is not null)
            {
                return (Tensor)ScriptModel.forward(input);
            }

            throw new InvalidOperationException("分类模型未加载");
        }

        public void Dispose()
        {
            ScriptModel?.Dispose();
            ModuleModel?.Dispose();
        }
    }
}

public sealed record TorchSharpClsInferResult(
    ClassificationResult? Detection,
    int ImageWidth,
    int ImageHeight,
    string Device,
    double TotalMilliseconds,
    double ReadImageMilliseconds,
    double InferenceMilliseconds);
