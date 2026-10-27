namespace DeepVisionSharp.TorchSharp.Training;

/// <summary>
/// Tracks whether validation quality still improves and decides when to stop.
/// </summary>
/// <remarks>
/// Patience of 0 disables the check, which keeps the previous fixed-epoch behaviour for jobs that
/// do not opt in. A minimum delta prevents stopping on floating-point noise at the top of the range.
/// </remarks>
public sealed class EarlyStoppingTracker
{
    private readonly int _patience;
    private readonly double _minDelta;
    private double _best = double.NegativeInfinity;
    private int _badEpochs;

    public EarlyStoppingTracker(int patience, double minDelta = 1e-4)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(patience);
        _patience = patience;
        _minDelta = minDelta;
    }

    public bool IsEnabled => _patience > 0;

    public double Best => _best;

    public int BadEpochs => _badEpochs;

    public bool ShouldStop => IsEnabled && _badEpochs >= _patience;

    /// <summary>Records an epoch's metric; returns true when it improved enough to reset patience.</summary>
    public bool Report(double metric)
    {
        if (!IsEnabled)
        {
            return true;
        }

        if (metric > _best + _minDelta)
        {
            _best = metric;
            _badEpochs = 0;
            return true;
        }

        _badEpochs++;
        return false;
    }
}
