using System.Text;

namespace DeepVisionSharp.TorchSharp.IO;

/// <summary>
/// Replaces a file only after the new content is complete and flushed to disk.
/// </summary>
/// <remarks>
/// Training runs in-process (ADR 0003), so a native crash can end the process while a checkpoint or
/// its <c>.info</c> sidecar is being written. Writing to a sibling temp file, flushing it and then
/// renaming over the target means the target is always either the old or the new complete file.
/// The temp file sits in the target directory so the rename never crosses volumes.
/// </remarks>
public static class AtomicFileWriter
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Lets <paramref name="writeTemp"/> write a temp path (for APIs that only take a path, such as
    /// <c>Module.save</c>), then flushes and moves it over <paramref name="path"/>.
    /// </summary>
    public static void Write(string path, Action<string> writeTemp)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(writeTemp);

        var tempPath = CreateTempPath(path);
        try
        {
            writeTemp(tempPath);
            FlushToDisk(tempPath);
            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            DeleteQuietly(tempPath);
        }
    }

    public static void WriteAllText(string path, string contents)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var tempPath = CreateTempPath(path);
        try
        {
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var bytes = Utf8NoBom.GetBytes(contents);
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            DeleteQuietly(tempPath);
        }
    }

    public static async Task WriteAllTextAsync(string path, string contents, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var tempPath = CreateTempPath(path);
        try
        {
            await using (var stream = new FileStream(
                             tempPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 4096,
                             useAsync: true))
            {
                await stream.WriteAsync(Utf8NoBom.GetBytes(contents), cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            DeleteQuietly(tempPath);
        }
    }

    private static string CreateTempPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new ArgumentException("目标路径缺少目录。", nameof(path));
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
    }

    /// <summary>FlushFileBuffers covers data written through any handle, so reopen and flush.</summary>
    private static void FlushToDisk(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        stream.Flush(flushToDisk: true);
    }

    private static void DeleteQuietly(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
