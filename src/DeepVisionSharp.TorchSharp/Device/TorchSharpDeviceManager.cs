using TorchSharp;
using Microsoft.Extensions.Logging;

namespace DeepVisionSharp.TorchSharp.Device;

/// <summary>
/// TorchSharp 设备探测与管理（支持 CUDA 与 CPU 自动降级与线程安全获取）。
/// </summary>
public sealed class TorchSharpDeviceManager
{
    private readonly ILogger<TorchSharpDeviceManager>? _logger;
    private static readonly object _initLock = new();
    private static bool _isCudaInitialized;
    private static bool _isCudaAvailable;

    public TorchSharpDeviceManager(ILogger<TorchSharpDeviceManager>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>检查当前系统 CUDA 是否可用。</summary>
    public static bool IsCudaAvailable
    {
        get
        {
            EnsureInitialized();
            return _isCudaAvailable;
        }
    }

    /// <summary>根据设备字符串（"CPU", "GPU", "CUDA", "AUTO"）解析对应的 TorchSharp Device。</summary>
    public static torch.Device ResolveDevice(string? deviceName = null)
    {
        EnsureInitialized();
        if (string.IsNullOrWhiteSpace(deviceName) || deviceName.Equals("AUTO", StringComparison.OrdinalIgnoreCase))
        {
            return _isCudaAvailable ? torch.CUDA : torch.CPU;
        }

        var lower = deviceName.Trim().ToLowerInvariant();
        if (lower is "gpu" or "cuda" or "cuda:0")
        {
            return _isCudaAvailable ? torch.CUDA : torch.CPU;
        }

        return torch.CPU;
    }

    private static void EnsureInitialized()
    {
        if (_isCudaInitialized)
        {
            return;
        }

        lock (_initLock)
        {
            if (_isCudaInitialized)
            {
                return;
            }

            try
            {
                _isCudaAvailable = torch.cuda.is_available();
            }
            catch
            {
                _isCudaAvailable = false;
            }

            _isCudaInitialized = true;
        }
    }
}
