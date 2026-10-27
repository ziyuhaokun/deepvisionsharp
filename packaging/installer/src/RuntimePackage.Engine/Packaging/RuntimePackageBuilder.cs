using RuntimePackage.Engine.Models;
using RuntimePackage.Engine.Packaging.Build;
using RuntimePackage.Engine.Serialization;
using RuntimePackage.Engine.Services;
using System.IO.Compression;

namespace RuntimePackage.Engine.Packaging;

public sealed class RuntimePackageBuilder
{
    public async Task<PackagerResult> BuildAsync(
        PackagerOptions options,
        IProgress<BuildProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        var context = BuildContext.Create(options);
        var cleanupCompleted = false;

        try
        {
            await RunInputValidationAsync(context, progress, cancellationToken);
            await ScanRuntimeAsync(context, progress, cancellationToken);
            await CopyRuntimeAsync(context, progress, cancellationToken);
            await CreateRuntimeZipAsync(context, progress, cancellationToken);
            await CopyMetadataAsync(context, progress, cancellationToken);
            await GenerateManifestAsync(context, progress, cancellationToken);
            await AssemblePackageAsync(context, progress, cancellationToken);
            cleanupCompleted = CleanupSuccessArtifacts(context);
            ReportProgress(
                context,
                progress,
                BuildStage.CleanupAndFinalize,
                "正在清理临时文件",
                100,
                100,
                1,
                1,
                1,
                1,
                "build succeeded");

            return new PackagerResult(
                PackagerOutcome.Success,
                context.OutputPackagePath,
                context.PayloadSha256,
                context.PayloadSizeBytes,
                context.LogFilePath,
                cleanupCompleted,
                $"已生成：{context.OutputPackagePath}");
        }
        catch (OperationCanceledException)
        {
            cleanupCompleted = CleanupArtifacts(context);
            ReportProgress(
                context,
                progress,
                BuildStage.CleanupAndFinalize,
                "正在清理临时文件",
                context.LastOverallPercent,
                100,
                1,
                1,
                1,
                1,
                "build cancelled");

            return new PackagerResult(
                PackagerOutcome.Cancelled,
                context.OutputPackagePath,
                string.Empty,
                0,
                context.LogFilePath,
                cleanupCompleted,
                "打包已取消，临时文件已清理。");
        }
        catch (Exception ex)
        {
            cleanupCompleted = CleanupArtifacts(context);
            ReportProgress(
                context,
                progress,
                BuildStage.CleanupAndFinalize,
                "正在清理临时文件",
                context.LastOverallPercent,
                100,
                1,
                1,
                1,
                1,
                $"build failed: {ex.Message}",
                isError: true);

            return new PackagerResult(
                PackagerOutcome.Failure,
                context.OutputPackagePath,
                string.Empty,
                0,
                context.LogFilePath,
                cleanupCompleted,
                $"打包失败：{ex.Message}");
        }
        finally
        {
            FlushLog(context);
        }
    }

    private static Task RunInputValidationAsync(
        BuildContext context,
        IProgress<BuildProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ValidateRequiredDirectory(context.RuntimeDirectory, nameof(context.RuntimeDirectory));
        ValidateRequiredDirectory(context.AssetsDirectory, nameof(context.AssetsDirectory));
        ValidateRequiredDirectory(context.VersionDirectory, nameof(context.VersionDirectory));

        if (IsPythonRuntime(context.Options.RuntimeType))
        {
            ValidateRequiredDirectory(context.RequirementsDirectory, nameof(context.RequirementsDirectory));
        }
        else if (!string.IsNullOrWhiteSpace(context.RequirementsDirectory) &&
                 !Directory.Exists(context.RequirementsDirectory))
        {
            throw new DirectoryNotFoundException($"Required directory not found: {context.RequirementsDirectory}");
        }

        ValidateRequiredFile(Path.Combine(context.AssetsDirectory, "license.txt"));
        ValidateRequiredFile(Path.Combine(context.AssetsDirectory, "logo.ico"));
        ValidateRequiredFile(Path.Combine(context.VersionDirectory, "version.txt"));

        Directory.CreateDirectory(context.StagingRoot);
        Directory.CreateDirectory(context.PayloadSourceDirectory);
        Directory.CreateDirectory(context.PackageRootDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(context.LogFilePath)!);

        ReportProgress(
            context,
            progress,
            BuildStage.InputValidation,
            "正在校验输入",
            5,
            100,
            1,
            1,
            1,
            1,
            "input validation complete");

        return Task.CompletedTask;
    }

    private static Task ScanRuntimeAsync(
        BuildContext context,
        IProgress<BuildProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        context.RuntimeFiles = Directory
            .GetFiles(context.RuntimeDirectory, "*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        context.TotalRuntimeFiles = context.RuntimeFiles.Length;
        context.TotalRuntimeBytes = context.RuntimeFiles.Sum(path => new FileInfo(path).Length);

        ReportProgress(
            context,
            progress,
            BuildStage.RuntimeScan,
            "正在扫描运行时内容",
            15,
            100,
            context.TotalRuntimeFiles,
            context.TotalRuntimeFiles,
            context.TotalRuntimeBytes,
            context.TotalRuntimeBytes,
            $"runtime scan complete: {context.TotalRuntimeFiles} files");

        return Task.CompletedTask;
    }

    private static async Task CopyRuntimeAsync(
        BuildContext context,
        IProgress<BuildProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        var processedItems = 0;
        long processedBytes = 0;

        await Parallel.ForEachAsync(
            context.RuntimeFiles,
            new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = ResolveRuntimeCopyParallelism()
            },
            async (file, token) =>
            {
                token.ThrowIfCancellationRequested();

                var fileBytes = new FileInfo(file).Length;
                var relativePath = Path.GetRelativePath(context.RuntimeDirectory, file);
                var destinationPath = Path.Combine(context.PayloadSourceDirectory, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

                await using var source = new FileStream(
                    file,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 131072,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                await using var destination = new FileStream(
                    destinationPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 131072,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                await source.CopyToAsync(destination, token);

                var currentItems = Interlocked.Increment(ref processedItems);
                var currentBytes = Interlocked.Add(ref processedBytes, fileBytes);

                ReportProgress(
                    context,
                    progress,
                    BuildStage.CopyRuntimeToStaging,
                    "正在复制运行时",
                    CalculateOverallPercent(15, 25, currentBytes, context.TotalRuntimeBytes),
                    CalculateStagePercent(currentBytes, context.TotalRuntimeBytes),
                    currentItems,
                    context.TotalRuntimeFiles,
                    currentBytes,
                    context.TotalRuntimeBytes,
                    $"copied {relativePath}");
            });

        if (context.TotalRuntimeFiles == 0)
        {
            ReportProgress(
                context,
                progress,
                BuildStage.CopyRuntimeToStaging,
                "正在复制运行时",
                40,
                100,
                0,
                0,
                0,
                0,
                "copied 0 files");
        }
    }

    private static async Task CreateRuntimeZipAsync(
        BuildContext context,
        IProgress<BuildProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        var stagedFiles = Directory
            .GetFiles(context.PayloadSourceDirectory, "*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var totalBytes = stagedFiles.Sum(path => new FileInfo(path).Length);
        var processedItems = 0;
        long processedBytes = 0;

        Directory.CreateDirectory(Path.GetDirectoryName(context.RuntimeZipPath)!);
        if (File.Exists(context.RuntimeZipPath))
        {
            File.Delete(context.RuntimeZipPath);
        }

        using (var archive = ZipFile.Open(context.RuntimeZipPath, ZipArchiveMode.Create))
        {
            foreach (var file in stagedFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var relativePath = Path.GetRelativePath(context.PayloadSourceDirectory, file);
                var archivePath = relativePath.Replace(Path.DirectorySeparatorChar, '/');
                var entry = archive.CreateEntry(archivePath, CompressionLevel.Fastest);
                await using var entryStream = entry.Open();
                await using var fileStream = File.OpenRead(file);
                await fileStream.CopyToAsync(entryStream, cancellationToken);

                processedItems++;
                processedBytes += new FileInfo(file).Length;

                ReportProgress(
                    context,
                    progress,
                    BuildStage.CreateRuntimeZip,
                    "正在压缩 runtime.zip",
                    CalculateOverallPercent(40, 30, processedBytes, totalBytes),
                    CalculateStagePercent(processedBytes, totalBytes),
                    processedItems,
                    stagedFiles.Length,
                    processedBytes,
                    totalBytes,
                    $"compressed {relativePath}");
            }
        }

        context.PayloadSha256 = await ChecksumService.ComputeSha256Async(context.RuntimeZipPath, cancellationToken);
        context.PayloadSizeBytes = new FileInfo(context.RuntimeZipPath).Length;

        if (stagedFiles.Length == 0)
        {
            ReportProgress(
                context,
                progress,
                BuildStage.CreateRuntimeZip,
                "正在压缩 runtime.zip",
                70,
                100,
                0,
                0,
                0,
                0,
                "compressed 0 files");
        }
    }

    private static Task CopyMetadataAsync(
        BuildContext context,
        IProgress<BuildProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Directory.CreateDirectory(context.AssetsOutputDirectory);
        Directory.CreateDirectory(context.MetadataOutputDirectory);

        File.Copy(Path.Combine(context.AssetsDirectory, "license.txt"), Path.Combine(context.AssetsOutputDirectory, "license.txt"), overwrite: true);
        File.Copy(Path.Combine(context.AssetsDirectory, "logo.ico"), Path.Combine(context.AssetsOutputDirectory, "logo.ico"), overwrite: true);
        var logoPngPath = Path.Combine(context.AssetsDirectory, "logo.png");
        if (File.Exists(logoPngPath))
        {
            File.Copy(logoPngPath, Path.Combine(context.AssetsOutputDirectory, "logo.png"), overwrite: true);
        }

        File.Copy(Path.Combine(context.VersionDirectory, "version.txt"), Path.Combine(context.MetadataOutputDirectory, "version.txt"), overwrite: true);

        if (!string.IsNullOrWhiteSpace(context.RequirementsDirectory) && Directory.Exists(context.RequirementsDirectory))
        {
            foreach (var requirementPath in Directory.GetFiles(context.RequirementsDirectory, "*.txt", SearchOption.TopDirectoryOnly))
            {
                File.Copy(requirementPath, Path.Combine(context.MetadataOutputDirectory, Path.GetFileName(requirementPath)), overwrite: true);
            }
        }

        ReportProgress(
            context,
            progress,
            BuildStage.CopyAssetsAndMetadata,
            "正在复制资源和元数据",
            75,
            100,
            1,
            1,
            1,
            1,
            "assets and metadata copied");

        return Task.CompletedTask;
    }

    private static async Task GenerateManifestAsync(
        BuildContext context,
        IProgress<BuildProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var version = await ResolvePackageVersionAsync(
            context.Options.Version,
            Path.Combine(context.VersionDirectory, "version.txt"),
            cancellationToken);

        var runtimeType = string.IsNullOrWhiteSpace(context.Options.RuntimeType)
            ? "python"
            : context.Options.RuntimeType.Trim();

        var requirementEntries = Directory
            .GetFiles(context.MetadataOutputDirectory, "requirements*.txt", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => $"metadata/{name}")
            .ToArray()!;

        var entryPoints = ResolveEntryPoints(context.Options, runtimeType);
        var environmentVariables = ResolveEnvironmentVariables(context.Options, runtimeType);

        var manifest = new PackageManifest
        {
            SchemaVersion = 1,
            PackageId = context.Options.PackageId,
            ProductName = context.Options.ProductName,
            Version = version,
            Publisher = context.Options.Publisher,
            RuntimeType = runtimeType,
            Architecture = context.Options.Architecture,
            RequiresAdmin = context.Options.RequiresAdmin,
            CreatedAtUtc = DateTime.UtcNow,
            Payload = new PayloadDescriptor("runtime.zip", context.PayloadSha256, context.PayloadSizeBytes),
            Assets = new AssetDescriptor("assets/license.txt", "assets/logo.ico"),
            Metadata = new MetadataDescriptor("metadata/version.txt", requirementEntries),
            Install = new InstallOptions(context.Options.DefaultInstallDirectory, true, false, context.Options.ProductName),
            EnvironmentVariables = environmentVariables,
            EntryPoints = entryPoints,
            Hooks = new HookDescriptor(null, null)
        };

        await File.WriteAllTextAsync(context.ManifestPath, ManifestSerializer.Serialize(manifest), cancellationToken);

        ReportProgress(
            context,
            progress,
            BuildStage.GenerateManifest,
            "正在生成 manifest.json",
            80,
            100,
            1,
            1,
            1,
            1,
            "manifest generated");
    }

    private static async Task AssemblePackageAsync(
        BuildContext context,
        IProgress<BuildProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        var packageFiles = Directory
            .GetFiles(context.PackageRootDirectory, "*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var totalBytes = packageFiles.Sum(path => new FileInfo(path).Length);
        var processedItems = 0;
        long processedBytes = 0;

        Directory.CreateDirectory(Path.GetDirectoryName(context.OutputPackagePath)!);
        if (File.Exists(context.TempOutputPackagePath))
        {
            File.Delete(context.TempOutputPackagePath);
        }

        using (var archive = ZipFile.Open(context.TempOutputPackagePath, ZipArchiveMode.Create))
        {
            foreach (var file in packageFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var relativePath = Path.GetRelativePath(context.PackageRootDirectory, file);
                var archivePath = relativePath.Replace(Path.DirectorySeparatorChar, '/');
                var entry = archive.CreateEntry(archivePath, CompressionLevel.Fastest);
                await using var entryStream = entry.Open();
                await using var fileStream = File.OpenRead(file);
                await fileStream.CopyToAsync(entryStream, cancellationToken);

                processedItems++;
                processedBytes += new FileInfo(file).Length;

                ReportProgress(
                    context,
                    progress,
                    BuildStage.AssemblePackage,
                    "正在组装最终安装包",
                    CalculateOverallPercent(80, 15, processedBytes, totalBytes),
                    CalculateStagePercent(processedBytes, totalBytes),
                    processedItems,
                    packageFiles.Length,
                    processedBytes,
                    totalBytes,
                    $"assembled {relativePath}");
            }
        }

        File.Move(context.TempOutputPackagePath, context.OutputPackagePath, overwrite: true);

        if (packageFiles.Length == 0)
        {
            ReportProgress(
                context,
                progress,
                BuildStage.AssemblePackage,
                "正在组装最终安装包",
                95,
                100,
                0,
                0,
                0,
                0,
                "assembled empty package");
        }
    }

    private static void ReportProgress(
        BuildContext context,
        IProgress<BuildProgressUpdate>? progress,
        BuildStage stage,
        string stageDisplayName,
        int overallPercent,
        int stagePercent,
        int processedItems,
        int totalItems,
        long processedBytes,
        long totalBytes,
        string message,
        bool isError = false)
    {
        var boundedOverall = Math.Clamp(overallPercent, 0, 100);
        var boundedStage = Math.Clamp(stagePercent, 0, 100);
        context.UpdateLastOverallPercent(boundedOverall);

        var update = new BuildProgressUpdate(
            stage,
            stageDisplayName,
            boundedOverall,
            boundedStage,
            processedItems,
            totalItems,
            processedBytes,
            totalBytes,
            message,
            context.LogFilePath,
            isError);

        progress?.Report(update);
        WriteLog(context, message, isError);
    }

    private static void WriteLog(BuildContext context, string message, bool isError)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(context.LogFilePath)!);
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{(isError ? "ERR" : "INF")}] {message}{Environment.NewLine}";
        lock (context.LogSync)
        {
            context.PendingLogLines.Add(line);
            if (context.PendingLogLines.Count >= BuildContext.LogFlushThreshold)
            {
                File.AppendAllText(context.LogFilePath, string.Concat(context.PendingLogLines));
                context.PendingLogLines.Clear();
            }
        }
    }

    private static void FlushLog(BuildContext context)
    {
        lock (context.LogSync)
        {
            if (context.PendingLogLines.Count == 0)
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(context.LogFilePath)!);
            File.AppendAllText(context.LogFilePath, string.Concat(context.PendingLogLines));
            context.PendingLogLines.Clear();
        }
    }

    private static int CalculateStagePercent(long processed, long total)
    {
        if (total <= 0)
        {
            return 100;
        }

        return (int)Math.Clamp(processed * 100L / total, 0, 100);
    }

    private static int CalculateOverallPercent(int stageStart, int stageWeight, long processed, long total)
    {
        if (total <= 0)
        {
            return stageStart + stageWeight;
        }

        return stageStart + (int)Math.Clamp(processed * stageWeight / total, 0, stageWeight);
    }

    private static int ResolveRuntimeCopyParallelism() =>
        Math.Clamp(Environment.ProcessorCount / 2, 2, 8);

    private static async Task<string> ResolvePackageVersionAsync(
        string? explicitVersion,
        string versionFilePath,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(explicitVersion))
        {
            return explicitVersion.Trim();
        }

        var rawText = await File.ReadAllTextAsync(versionFilePath, cancellationToken);
        foreach (var line in rawText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line.StartsWith("Software Version:", StringComparison.OrdinalIgnoreCase))
            {
                var value = line["Software Version:".Length..].Trim();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }

        var firstMeaningfulLine = rawText
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(line => !line.Contains(':'));

        if (!string.IsNullOrWhiteSpace(firstMeaningfulLine))
        {
            return firstMeaningfulLine;
        }

        return rawText.Trim();
    }

    private static bool CleanupSuccessArtifacts(BuildContext context)
    {
        var success = true;
        success &= TryDeleteFile(context.RuntimeZipPath);
        success &= TryDeleteDirectory(context.StagingRoot);
        success &= TryDeleteFile(context.TempOutputPackagePath);
        return success;
    }

    private static bool CleanupArtifacts(BuildContext context)
    {
        var success = true;
        success &= TryDeleteFile(context.OutputPackagePath);
        success &= TryDeleteFile(context.TempOutputPackagePath);
        success &= TryDeleteFile(context.RuntimeZipPath);
        success &= TryDeleteDirectory(context.StagingRoot);
        return success;
    }

    private static bool TryDeleteFile(string path)
    {
        if (!File.Exists(path))
        {
            return true;
        }

        try
        {
            ClearReadOnlyAttribute(path);
            File.Delete(path);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryDeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return true;
        }

        try
        {
            ClearReadOnlyAttributes(path);
            Directory.Delete(path, true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsPythonRuntime(string? runtimeType) =>
        string.IsNullOrWhiteSpace(runtimeType) ||
        string.Equals(runtimeType.Trim(), "python", StringComparison.OrdinalIgnoreCase);

    private static List<EntryPointDescriptor> ResolveEntryPoints(PackagerOptions options, string runtimeType)
    {
        if (options.EntryPoints is { Count: > 0 })
        {
            return options.EntryPoints.ToList();
        }

        if (IsPythonRuntime(runtimeType))
        {
            return [new EntryPointDescriptor("python", "Python", "python.exe", string.Empty)];
        }

        throw new InvalidOperationException("app 类型打包必须通过 EntryPoints / --entry-point 指定入口。");
    }

    private static List<EnvironmentVariableDescriptor> ResolveEnvironmentVariables(
        PackagerOptions options,
        string runtimeType)
    {
        if (options.EnvironmentVariables is not null)
        {
            return options.EnvironmentVariables.ToList();
        }

        if (IsPythonRuntime(runtimeType))
        {
            return [new EnvironmentVariableDescriptor("In3Vision_Home", "{installDir}\\python.exe", "User")];
        }

        return [];
    }

    private static void ValidateRequiredDirectory(string path, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path, name);
        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException($"Directory not found: {path}");
        }
    }

    private static void ValidateRequiredFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"File not found: {path}", path);
        }
    }

    private static void ClearReadOnlyAttributes(string root)
    {
        try
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
                         .OrderByDescending(value => value.Length))
            {
                ClearReadOnlyAttribute(path);
            }

            ClearReadOnlyAttribute(root);
        }
        catch
        {
        }
    }

    private static void ClearReadOnlyAttribute(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
            }
        }
        catch
        {
        }
    }

    private sealed class BuildContext
    {
        public const int LogFlushThreshold = 64;
        private int _lastOverallPercent;

        private BuildContext(PackagerOptions options, string stagingRoot, string logFilePath)
        {
            Options = options;
            StagingRoot = stagingRoot;
            LogFilePath = logFilePath;
            RuntimeDirectory = options.RuntimeDirectory;
            AssetsDirectory = options.AssetsDirectory;
            RequirementsDirectory = options.RequirementsDirectory;
            VersionDirectory = options.VersionDirectory;
            OutputPackagePath = options.OutputPackagePath;
            PayloadSourceDirectory = Path.Combine(stagingRoot, "payload-source");
            PackageRootDirectory = Path.Combine(stagingRoot, "package-root");
            AssetsOutputDirectory = Path.Combine(PackageRootDirectory, "assets");
            MetadataOutputDirectory = Path.Combine(PackageRootDirectory, "metadata");
            RuntimeZipPath = Path.Combine(PackageRootDirectory, "runtime.zip");
            ManifestPath = Path.Combine(PackageRootDirectory, "manifest.json");
            TempOutputPackagePath = Path.Combine(stagingRoot, "output.vrp.tmp");
        }

        public PackagerOptions Options { get; }
        public string StagingRoot { get; }
        public string LogFilePath { get; }
        public string RuntimeDirectory { get; }
        public string AssetsDirectory { get; }
        public string RequirementsDirectory { get; }
        public string VersionDirectory { get; }
        public string OutputPackagePath { get; }
        public string TempOutputPackagePath { get; }
        public string PayloadSourceDirectory { get; }
        public string PackageRootDirectory { get; }
        public string AssetsOutputDirectory { get; }
        public string MetadataOutputDirectory { get; }
        public string RuntimeZipPath { get; }
        public string ManifestPath { get; }
        public string[] RuntimeFiles { get; set; } = [];
        public int TotalRuntimeFiles { get; set; }
        public long TotalRuntimeBytes { get; set; }
        public string PayloadSha256 { get; set; } = string.Empty;
        public long PayloadSizeBytes { get; set; }
        public int LastOverallPercent => Volatile.Read(ref _lastOverallPercent);
        public object LogSync { get; } = new();
        public List<string> PendingLogLines { get; } = new(LogFlushThreshold);

        public void UpdateLastOverallPercent(int value)
        {
            var current = LastOverallPercent;
            while (value > current)
            {
                var previous = Interlocked.CompareExchange(ref _lastOverallPercent, value, current);
                if (previous == current)
                {
                    return;
                }

                current = previous;
            }
        }

        public static BuildContext Create(PackagerOptions options)
        {
            var stagingRoot = Path.Combine(Path.GetTempPath(), "RuntimePackager", Guid.NewGuid().ToString("N"));
            var logFilePath = ResolveLogFilePath(options.OutputPackagePath);
            return new BuildContext(options, stagingRoot, logFilePath);
        }

        private static string ResolveLogFilePath(string outputPackagePath)
        {
            var fileName = string.IsNullOrWhiteSpace(outputPackagePath)
                ? $"runtime-packager-{DateTime.Now:yyyyMMdd-HHmmss}.build.log"
                : $"{Path.GetFileNameWithoutExtension(outputPackagePath)}.build.log";

            var outputDirectory = Path.GetDirectoryName(outputPackagePath);
            if (!string.IsNullOrWhiteSpace(outputDirectory))
            {
                try
                {
                    Directory.CreateDirectory(outputDirectory);
                    return Path.Combine(outputDirectory, fileName);
                }
                catch
                {
                }
            }

            var fallbackDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DeepVisionSharp",
                "Logs");
            Directory.CreateDirectory(fallbackDirectory);
            return Path.Combine(fallbackDirectory, fileName);
        }
    }
}
