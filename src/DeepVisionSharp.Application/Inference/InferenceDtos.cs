using DeepVisionSharp.Domain.Common;
using DeepVisionSharp.Domain.ValueObjects;

namespace DeepVisionSharp.Application.Inference;

/// <summary>
/// Supported labeling inference device ids. Only CPU and primary GPU (cuda:0) are product options.
/// </summary>
public static class InferenceDevices
{
    public const string Cpu = "cpu";

    /// <summary>Primary / top GPU (first CUDA device).</summary>
    public const string PrimaryGpu = "cuda:0";

    public static string Normalize(string? device)
    {
        if (string.IsNullOrWhiteSpace(device))
        {
            return PrimaryGpu;
        }

        var value = device.Trim().ToLowerInvariant();
        return value switch
        {
            "cpu" => Cpu,
            "cuda:0" or "cuda" or "gpu" or "0" => PrimaryGpu,
            _ => throw new ArgumentException(
                $"不支持的推理设备：{device}。仅支持 cpu 或 cuda:0。",
                nameof(device))
        };
    }

    public static bool TryNormalize(string? device, out string normalized)
    {
        try
        {
            normalized = Normalize(device);
            return true;
        }
        catch
        {
            normalized = PrimaryGpu;
            return false;
        }
    }

}

/// <summary>单张图片最多保留的推理结果条数。分类模型固定 top-1，此上限不参与计算。</summary>
public static class InferenceMaxDetectionsLimits
{
    public const int Min = 1;
    public const int Max = 1000;
    public const int Default = 100;

    public static int Normalize(int value) => Math.Clamp(value, Min, Max);

    /// <summary>按 TorchSharp 后端解析设备名：cpu / cuda:0。</summary>
    public static Result<string> NormalizeDevice(string? device)
    {
        return InferenceDevices.TryNormalize(device, out var torch)
            ? Result<string>.Success(torch)
            : Result<string>.Failure("推理设备无效，仅支持 CPU 或 cuda:0。");
    }
}

public sealed class InferenceConfig
{
    public Guid? ModelId { get; set; }

    public double ConfidenceThreshold { get; set; } = 0.5;

    public int MaxDetections { get; set; } = InferenceMaxDetectionsLimits.Default;

    public int BatchSize { get; set; } = 8;

    /// <summary>Inference device: <see cref="InferenceDevices.Cpu"/> / <see cref="InferenceDevices.PrimaryGpu"/>.</summary>
    public string Device { get; set; } = InferenceDevices.PrimaryGpu;

    /// <summary>
    /// Identifies who started the work, so <c>StopInferenceCommand</c> can stop exactly that caller's
    /// inference instead of everyone's. Not part of the inference parameters and ignored by
    /// <see cref="IsValid"/>; null means "anonymous" and such work can never be stopped by owner.
    /// </summary>
    public string? OwnerKey { get; set; }

    public bool IsValid =>
        ModelId.HasValue
        && ConfidenceThreshold is > 0 and <= 1
        && MaxDetections is >= InferenceMaxDetectionsLimits.Min and <= InferenceMaxDetectionsLimits.Max
        && BatchSize > 0
        && InferenceMaxDetectionsLimits.NormalizeDevice(Device).IsSuccess;
}

public sealed class InferenceResult
{
    public bool Success { get; set; }

    public string? Error { get; set; }

    public List<ClassificationResult> Detections { get; set; } = new();

    public InferencePerformanceMetrics? Performance { get; set; }

    /// <summary>
    /// Decoded image width in pixels; 0 when the backend did not report it. Write-back needs this
    /// because an unannotated sample has no stored dimensions to carry over.
    /// </summary>
    public int ImageWidth { get; set; }

    /// <summary>Decoded image height in pixels; 0 when the backend did not report it.</summary>
    public int ImageHeight { get; set; }
}

public sealed class InferencePerformanceMetrics
{
    public double TotalMilliseconds { get; set; }

    /// <summary>Shell-measured preflight wall time (includes cache hits near 0).</summary>
    public double PreflightMilliseconds { get; set; }

    /// <summary>Shell-measured model load wall time (0 when already loaded / skipped).</summary>
    public double LoadMilliseconds { get; set; }

    public double ReadImageMilliseconds { get; set; }

    public double InferenceMilliseconds { get; set; }

    public int ResultCount { get; set; }

    public int ImageWidth { get; set; }

    public int ImageHeight { get; set; }

    public string Device { get; set; } = "cpu";

    public bool CudaAvailable { get; set; }

    public string? CudaVersion { get; set; }

    public string? GpuDeviceName { get; set; }

    public bool PreflightFromCache { get; set; }

    public bool LoadSkipped { get; set; }

    public string GetSummary()
    {
        var parts = new List<string>
        {
            $"总 {TotalMilliseconds:F1}ms"
        };

        if (PreflightMilliseconds > 0.05 || PreflightFromCache)
        {
            parts.Add(PreflightFromCache
                ? $"预检缓存 {PreflightMilliseconds:F1}ms"
                : $"预检 {PreflightMilliseconds:F1}ms");
        }

        if (LoadSkipped)
        {
            parts.Add("加载 跳过");
        }
        else if (LoadMilliseconds > 0.05)
        {
            parts.Add($"加载 {LoadMilliseconds:F1}ms");
        }

        if (InferenceMilliseconds > 0.05)
        {
            parts.Add($"推理 {InferenceMilliseconds:F1}ms");
        }

        if (ResultCount > 0)
        {
            parts.Add($"结果 {ResultCount}");
        }

        if (!string.IsNullOrWhiteSpace(Device))
        {
            parts.Add(Device);
        }

        return string.Join(" · ", parts);
    }
}


