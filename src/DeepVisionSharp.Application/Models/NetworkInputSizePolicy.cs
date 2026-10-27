using DeepVisionSharp.Domain.ValueObjects;

namespace DeepVisionSharp.Application.Models;

/// <summary>
/// Product range for network input edge length (imgsz).
/// Attached to <see cref="ResolvedTaskProfile"/> when <c>UsesNetworkInputSize</c> is true.
/// </summary>
public sealed record NetworkInputSizePolicy(int Min, int Max, int Step, int Default)
{
    /// <summary>Classification product band.</summary>
    public static NetworkInputSizePolicy ClassificationProduct { get; } = new(
        Min: 64,
        Max: 2048,
        Step: 32,
        // 256 keeps the first stage's activation footprint manageable; 512 is available but costs
        // 4x the memory and compute at the same batch size.
        Default: 256);

    public static NetworkInputSizePolicy? ForModelFamily(ModelFamily family) =>
        family == ModelFamily.Classification ? ClassificationProduct : null;

    public static NetworkInputSizePolicy? ForModelType(string? modelType)
    {
        if (!ModelCapabilityCatalog.TryResolve(modelType, out var capabilities))
        {
            return null;
        }

        return ForModelFamily(capabilities.Family);
    }

    public bool IsValid(int value) =>
        value >= Min
        && value <= Max
        && value % Step == 0;

    /// <summary>Clamp to [Min, Max] and align down to Step (never below Min).</summary>
    public int Normalize(int raw)
    {
        var clamped = Math.Clamp(raw, Min, Max);
        var aligned = clamped - (clamped % Step);
        return aligned < Min ? Min : aligned;
    }
}
