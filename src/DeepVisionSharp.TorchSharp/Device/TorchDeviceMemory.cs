using System.Reflection;
using System.Runtime.InteropServices;

using Microsoft.Extensions.Logging;

namespace DeepVisionSharp.TorchSharp.Device;

/// <summary>
/// Returns unused device memory to the driver.
/// </summary>
/// <remarks>
/// PyTorch's caching allocator keeps freed blocks for reuse, so disposing a model does not lower the
/// process's GPU footprint and a following training run can fail to allocate even though nothing is
/// resident. TorchSharp 0.107 does not expose <c>torch.cuda.empty_cache()</c> — its <c>torch.cuda</c>
/// surface is only is_available / is_cudnn_available / device_count / manual_seed* / synchronize —
/// so this calls the native entry point that PyTorch's own <c>empty_cache</c> delegates to:
/// <c>at::accelerator::emptyCache()</c>, exported by <c>torch_cpu.dll</c>.
/// Every failure path degrades to a no-op: a missing symbol must never break inference or training.
/// </remarks>
public static class TorchDeviceMemory
{
    private const string EmptyCacheSymbol = "?emptyCache@accelerator@at@@YAXXZ";
    private const string TorchCpuLibraryName = "torch_cpu.dll";

    private static readonly object Sync = new();
    private static EmptyCacheDelegate? _emptyCache;
    private static bool _resolved;

    /// <summary>Releases cached device memory. Safe to call on any platform and any device.</summary>
    public static void EmptyCache(ILogger? logger = null)
    {
        var emptyCache = Resolve(logger);
        if (emptyCache is null)
        {
            return;
        }

        try
        {
            emptyCache();
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "Releasing cached device memory failed");
        }
    }

    private static EmptyCacheDelegate? Resolve(ILogger? logger)
    {
        if (_resolved)
        {
            return _emptyCache;
        }

        lock (Sync)
        {
            if (_resolved)
            {
                return _emptyCache;
            }

            _resolved = true;
            try
            {
                var libraryPath = LocateTorchCpu(logger);
                if (libraryPath is null)
                {
                    return null;
                }

                // TorchSharp has already loaded this library by the time any tensor exists; loading
                // it again by path returns the same module and does not re-initialise anything.
                var handle = NativeLibrary.Load(libraryPath);
                if (!NativeLibrary.TryGetExport(handle, EmptyCacheSymbol, out var address))
                {
                    logger?.LogDebug(
                        "torch_cpu.dll does not export {Symbol}; cached device memory cannot be released",
                        EmptyCacheSymbol);
                    return null;
                }

                _emptyCache = Marshal.GetDelegateForFunctionPointer<EmptyCacheDelegate>(address);
                return _emptyCache;
            }
            catch (Exception ex)
            {
                logger?.LogDebug(ex, "Could not resolve the native device-memory release entry point");
                return null;
            }
        }
    }

    /// <summary>
    /// Finds the torch_cpu.dll that belongs to the already-loaded native runtime. TorchSharp resolves
    /// it from its own package layout, so the file is not necessarily beside the executable.
    /// </summary>
    private static string? LocateTorchCpu(ILogger? logger)
    {
        var candidates = new List<string>();

        // 1. The directory of the native library TorchSharp actually loaded.
        try
        {
            var torchSharpAssembly = Assembly.Load("TorchSharp");
            var nativePath = torchSharpAssembly.Location;
            if (!string.IsNullOrWhiteSpace(nativePath))
            {
                candidates.Add(Path.Combine(Path.GetDirectoryName(nativePath)!, TorchCpuLibraryName));
            }
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "Could not locate the TorchSharp assembly directory");
        }

        // 2. The application's runtime asset folders (runtimes/win-x64/native).
        var baseDirectory = AppContext.BaseDirectory;
        candidates.Add(Path.Combine(baseDirectory, TorchCpuLibraryName));
        try
        {
            candidates.AddRange(
                Directory.EnumerateFiles(baseDirectory, TorchCpuLibraryName, SearchOption.AllDirectories));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger?.LogDebug(ex, "Could not enumerate {FileName} under {BaseDirectory}", TorchCpuLibraryName, baseDirectory);
        }

        // 3. Already loaded in the process under a name the loader resolved for us.
        if (NativeLibrary.TryLoad(TorchCpuLibraryName, out _))
        {
            return TorchCpuLibraryName;
        }

        return candidates.FirstOrDefault(File.Exists);
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void EmptyCacheDelegate();
}
