namespace DeepVisionSharp.Shell.Services;

public sealed class SingleInstanceGuard : IDisposable
{
    /// <summary>
    /// Per-session name (no <c>Global\</c> prefix). Application data lives under
    /// <c>%LOCALAPPDATA%</c>, which is per user, so a machine-wide mutex would block a second user
    /// from running the app even though the two instances share no state.
    /// </summary>
    private const string MutexName = "Local\\Vision.DeepVisionSharp.WpfShell.SingleInstance";
    private readonly Mutex _mutex;
    private readonly bool _ownsMutex;

    private SingleInstanceGuard(Mutex mutex, bool ownsMutex)
    {
        _mutex = mutex;
        _ownsMutex = ownsMutex;
    }

    public static bool TryAcquire(out SingleInstanceGuard? guard)
    {
        var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (createdNew)
        {
            guard = new SingleInstanceGuard(mutex, ownsMutex: true);
            return true;
        }

        mutex.Dispose();
        guard = null;
        return false;
    }

    public void Dispose()
    {
        if (_ownsMutex)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch
            {
            }
        }

        _mutex.Dispose();
    }
}
