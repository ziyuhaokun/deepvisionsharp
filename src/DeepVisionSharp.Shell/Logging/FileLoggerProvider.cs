using System.Collections.Concurrent;
using System.IO;

using Microsoft.Extensions.Logging;
using DeepVisionSharp.Presentation.Logging;

namespace DeepVisionSharp.Shell.Logging;

public sealed class FileLoggerProvider : ILoggerProvider
{
    /// <summary>Legacy global lock kept for callers that still coordinate with file writes.</summary>
    public static readonly object FileLock = new();

    private static readonly ConcurrentDictionary<string, object> PathLocks = new(StringComparer.OrdinalIgnoreCase);

    private readonly FileLoggerOptions _options;

    public FileLoggerProvider(FileLoggerOptions options)
    {
        _options = options;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new FileLogger(categoryName, _options);
    }

    public void Dispose()
    {
    }

    /// <summary>
    /// Thread-safe append. Creates parent directories. Never throws to callers that wrap it;
    /// callers may still catch. Uses per-path locks to reduce multi-stage contention.
    /// </summary>
    public static void AppendToFile(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var gate = PathLocks.GetOrAdd(path, static _ => new object());
        lock (gate)
        {
            File.AppendAllText(path, content);
        }
    }

    public static void WriteAllText(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var gate = PathLocks.GetOrAdd(path, static _ => new object());
        lock (gate)
        {
            File.WriteAllText(path, content);
        }
    }
}
