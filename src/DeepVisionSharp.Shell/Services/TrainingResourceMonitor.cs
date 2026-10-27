using Microsoft.Extensions.Logging;

using System.Diagnostics;

namespace DeepVisionSharp.Shell.Services;

public sealed class TrainingResourceUsage
{
    public double CpuPercent { get; set; }

    public double MemoryPercent { get; set; }

    public double GpuUtilization { get; set; }

    public bool HasGpu { get; set; }
}

public sealed class TrainingResourceMonitor
{
    private readonly ILogger<TrainingResourceMonitor> _logger;

    public TrainingResourceMonitor(ILogger<TrainingResourceMonitor> logger)
    {
        _logger = logger;
    }

    public async Task<TrainingResourceUsage> GetUsageAsync(CancellationToken cancellationToken = default)
    {
        var usage = new TrainingResourceUsage();

        try
        {
            await Task.Run(() =>
            {
                using var cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                cpuCounter.NextValue();
                Thread.Sleep(100);
                usage.CpuPercent = Math.Round(cpuCounter.NextValue(), 1);

                using var memCounter = new PerformanceCounter("Memory", "% Committed Bytes In Use");
                usage.MemoryPercent = Math.Round(memCounter.NextValue(), 1);
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read CPU or memory performance counters.");
        }

        try
        {
            var gpu = await GetPrimaryGpuUtilizationAsync(cancellationToken).ConfigureAwait(false);
            if (gpu is not null)
            {
                usage.HasGpu = true;
                usage.GpuUtilization = gpu.Value;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to read GPU utilization.");
        }

        return usage;
    }

    private static async Task<double?> GetPrimaryGpuUtilizationAsync(CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "nvidia-smi",
            Arguments = "--query-gpu=utilization.gpu --format=csv,noheader,nounits",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process is null)
        {
            return null;
        }

        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            return null;
        }

        var line = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
        return double.TryParse(line, out var util) ? Math.Round(util, 1) : null;
    }
}
