namespace RuntimePackage.Engine.Installation;

/// <summary>Stages program files on the install volume and retains the previous files until commit.</summary>
internal sealed class InstallDirectorySwap
{
    private readonly string _installDirectory;
    private readonly string? _sharedUserDataRoot;
    private readonly bool _sharedDirectory;
    private readonly List<string> _backedUpEntries = [];
    private readonly List<string> _promotedEntries = [];
    private bool _createdInstallDirectory;
    private bool _backedUpDirectory;
    private bool _promotedDirectory;

    public InstallDirectorySwap(string installDirectory, string? sharedUserDataRoot)
    {
        _installDirectory = installDirectory;
        _sharedUserDataRoot = sharedUserDataRoot;
        _sharedDirectory = ProtectedUserDataPaths.IsSharedUserDataDirectory(installDirectory, sharedUserDataRoot);
        var parent = Path.GetDirectoryName(installDirectory)
            ?? throw new InvalidOperationException("安装目录必须有父目录。");
        WorkDirectory = Path.Combine(parent, $".runtimepackage-install-{Guid.NewGuid():N}");
    }

    public string WorkDirectory { get; }

    public string StagingDirectory => Path.Combine(WorkDirectory, "staged");

    private string BackupDirectory => Path.Combine(WorkDirectory, "previous");

    public void Promote(bool allowOverwrite, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var liveExists = Directory.Exists(_installDirectory);
        if (liveExists && (File.GetAttributes(_installDirectory) & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException("安装目录不能是目录链接或重解析点。");
        }

        var oldEntries = liveExists
            ? Directory.GetFileSystemEntries(_installDirectory)
                .Where(path => !ProtectedUserDataPaths.IsProtectedTopLevelEntry(_installDirectory, path, _sharedUserDataRoot))
                .ToArray()
            : [];
        if (oldEntries.Length > 0 && !allowOverwrite)
        {
            throw new InvalidOperationException("安装目录不是空目录。");
        }

        if (!_sharedDirectory)
        {
            if (liveExists)
            {
                Directory.Move(_installDirectory, BackupDirectory);
                _backedUpDirectory = true;
            }

            Directory.Move(StagingDirectory, _installDirectory);
            _promotedDirectory = true;
            return;
        }

        // A legacy install shares its root with user data. Those directories never move.
        var newEntries = Directory.GetFileSystemEntries(StagingDirectory);
        foreach (var path in newEntries)
        {
            var target = Path.Combine(_installDirectory, Path.GetFileName(path));
            if (ProtectedUserDataPaths.IsProtectedTopLevelEntry(_installDirectory, target, _sharedUserDataRoot))
            {
                throw new IOException($"安装包内容与受保护的用户数据目录冲突：{target}");
            }
        }

        if (!liveExists)
        {
            Directory.CreateDirectory(_installDirectory);
            _createdInstallDirectory = true;
        }

        Directory.CreateDirectory(BackupDirectory);
        foreach (var path in oldEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = Path.GetFileName(path);
            MoveEntry(path, Path.Combine(BackupDirectory, name));
            _backedUpEntries.Add(name);
        }

        foreach (var path in newEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = Path.GetFileName(path);
            MoveEntry(path, Path.Combine(_installDirectory, name));
            _promotedEntries.Add(name);
        }
    }

    public void Rollback()
    {
        if (_promotedDirectory)
        {
            Directory.Move(_installDirectory, StagingDirectory);
        }

        if (_backedUpDirectory)
        {
            Directory.Move(BackupDirectory, _installDirectory);
        }

        foreach (var name in _promotedEntries.AsEnumerable().Reverse())
        {
            MoveEntry(Path.Combine(_installDirectory, name), Path.Combine(StagingDirectory, name));
        }

        foreach (var name in _backedUpEntries.AsEnumerable().Reverse())
        {
            MoveEntry(Path.Combine(BackupDirectory, name), Path.Combine(_installDirectory, name));
        }

        if (_createdInstallDirectory && !Directory.EnumerateFileSystemEntries(_installDirectory).Any())
        {
            Directory.Delete(_installDirectory);
        }
    }

    private static void MoveEntry(string source, string destination)
    {
        if (Directory.Exists(source))
        {
            Directory.Move(source, destination);
        }
        else
        {
            File.Move(source, destination);
        }
    }
}
