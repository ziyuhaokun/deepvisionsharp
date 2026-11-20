namespace DeepVisionSharp.Presentation.Services;

/// <summary>One CPU / memory / GPU load sample shown on the training resource panel.</summary>
public sealed class TrainingResourceUsage
{
    public double CpuPercent { get; set; }

    public double MemoryPercent { get; set; }

    public double GpuUtilization { get; set; }

    public bool HasGpu { get; set; }
}
