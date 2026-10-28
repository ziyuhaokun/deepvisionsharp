using Xunit;

namespace DeepVisionSharp.Infrastructure.Tests;

/// <summary>
/// Waiting helpers that do not depend on wall-clock timing.
/// </summary>
/// <remarks>
/// "Sleep then assert it has not happened" is a race: on a loaded machine the work can arrive after
/// the sleep, and on a fast one the sleep is pure waste. Waiting for an observable condition instead
/// keeps the assertion about the behaviour, not about scheduling.
/// </remarks>
internal static class TestAsync
{
    /// <summary>
    /// Waits until <paramref name="condition"/> holds. Use it where a test needs "this has happened
    /// now" instead of a sleep: the condition should name the observable state the assertion is about.
    /// </summary>
    public static async Task WaitUntil(Func<bool> condition, TimeSpan timeout, string? because = null)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Yield();
        }

        Assert.Fail($"条件在 {timeout.TotalMilliseconds:F0}ms 内未满足。{because}");
    }
}
