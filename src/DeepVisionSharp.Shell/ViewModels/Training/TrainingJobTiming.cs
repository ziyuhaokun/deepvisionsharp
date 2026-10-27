namespace DeepVisionSharp.Shell.ViewModels.Training;

internal static class TrainingJobTiming
{
    public static TimeSpan ComputeElapsed(DateTime? startedAtUtc, DateTime? finishedAtUtc)
    {
        if (startedAtUtc is null)
        {
            return TimeSpan.Zero;
        }

        var start = AsUtc(startedAtUtc.Value);
        var end = finishedAtUtc is null ? DateTime.UtcNow : AsUtc(finishedAtUtc.Value);
        var elapsed = end - start;
        return elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
    }

    public static string FormatDuration(TimeSpan duration) =>
        duration.ToString(@"hh\:mm\:ss");

    public static string FormatStartedAtLocal(DateTime? startedAtUtc) =>
        startedAtUtc is null
            ? "—"
            : AsUtc(startedAtUtc.Value).ToLocalTime().ToString("MM-dd HH:mm");

    public static string FormatStartedAtLocalFull(DateTime? startedAtUtc) =>
        startedAtUtc is null
            ? "—"
            : AsUtc(startedAtUtc.Value).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    public static string FormatFinishedAtLocal(DateTime? finishedAtUtc) =>
        finishedAtUtc is null
            ? "—"
            : AsUtc(finishedAtUtc.Value).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    public static string FormatMetaText(DateTime? startedAtUtc, DateTime? finishedAtUtc)
    {
        if (startedAtUtc is null)
        {
            return "尚未开始";
        }

        return $"{FormatStartedAtLocal(startedAtUtc)} · {FormatDuration(ComputeElapsed(startedAtUtc, finishedAtUtc))}";
    }

    private static DateTime AsUtc(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
}
