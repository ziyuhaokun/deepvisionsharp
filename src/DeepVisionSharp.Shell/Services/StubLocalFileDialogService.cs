using System.IO;
using DeepVisionSharp.Application.Galleries;
using DeepVisionSharp.Application.Ports;

namespace DeepVisionSharp.Shell.Services;

/// <summary>
/// File/folder dialog stub for <c>--ui-test</c>. Reads queued scenarios from
/// <c>{DataRoot}/.ui-test/</c> so FlaUI never has to drive Win32 file dialogs.
/// </summary>
public sealed class StubLocalFileDialogService : ILocalFileDialogService
{
    private readonly IDataRootProvider _dataRoot;
    private readonly object _gate = new();

    public StubLocalFileDialogService(IDataRootProvider dataRoot)
    {
        _dataRoot = dataRoot;
    }

    public Task<string[]?> SelectFilesAsync(string filter = "所有文件|*.*", bool allowMultiSelect = true)
    {
        lock (_gate)
        {
            return Task.FromResult(ReadPaths("select-files.txt"));
        }
    }

    public Task<string?> SaveFileAsync(string filter, string defaultFileName)
    {
        lock (_gate)
        {
            var paths = ReadPaths("save-file.txt");
            return Task.FromResult(paths is { Length: > 0 } ? paths[0] : null);
        }
    }

    public Task<string?> SelectFolderAsync()
    {
        lock (_gate)
        {
            var paths = ReadPaths("select-folder.txt");
            return Task.FromResult(paths is { Length: > 0 } ? paths[0] : null);
        }
    }

    private string[]? ReadPaths(string fileName)
    {
        var path = Path.Combine(_dataRoot.DataDirectory, ".ui-test", fileName);
        if (!File.Exists(path))
        {
            return null;
        }

        var lines = File.ReadAllLines(path)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToArray();

        try
        {
            File.Delete(path);
        }
        catch
        {
            // Ignore delete races; next read can overwrite.
        }

        return lines.Length == 0 ? null : lines;
    }
}
