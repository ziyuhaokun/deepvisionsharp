using RuntimePackage.Engine.Packaging.Build;

namespace RuntimePackage.Engine.Packaging;

public sealed class SetupBundleBuilder
{
    private readonly RuntimePackageBuilder _packageBuilder;

    public SetupBundleBuilder()
        : this(new RuntimePackageBuilder())
    {
    }

    internal SetupBundleBuilder(RuntimePackageBuilder packageBuilder)
    {
        _packageBuilder = packageBuilder;
    }

    public async Task<PackagerResult> BuildAsync(
        SetupBundleOptions options,
        IProgress<BuildProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.PackageOptions);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.SetupHostExecutablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.OutputSetupPath);

        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(options.SetupHostExecutablePath))
        {
            throw new FileNotFoundException("SetupHost executable not found.", options.SetupHostExecutablePath);
        }

        var packageOptions = ClonePackageOptions(options.PackageOptions);
        var intermediatePackagePath = ResolveIntermediatePackagePath(packageOptions.OutputPackagePath, options.OutputSetupPath);
        packageOptions.OutputPackagePath = intermediatePackagePath;

        var packageResult = await _packageBuilder.BuildAsync(packageOptions, progress, cancellationToken);
        if (!packageResult.Success)
        {
            return packageResult;
        }

        try
        {
            await SetupBundleAssembler.AssembleAsync(
                options.SetupHostExecutablePath,
                intermediatePackagePath,
                options.OutputSetupPath,
                cancellationToken);

            ReportProgress(
                progress,
                "正在生成 Setup.exe",
                100,
                100,
                1,
                1,
                new FileInfo(options.OutputSetupPath).Length,
                new FileInfo(options.OutputSetupPath).Length,
                packageResult.LogFilePath,
                $"setup bundle assembled: {options.OutputSetupPath}");

            if (!options.KeepIntermediatePackage)
            {
                TryDeleteFile(intermediatePackagePath);
            }

            var summary = options.KeepIntermediatePackage
                ? $"已生成安装器：{options.OutputSetupPath}，中间负载：{intermediatePackagePath}"
                : $"已生成安装器：{options.OutputSetupPath}";

            return packageResult with
            {
                PackagePath = options.OutputSetupPath,
                SummaryMessage = summary
            };
        }
        catch (Exception ex)
        {
            TryDeleteFile(options.OutputSetupPath);
            ReportProgress(
                progress,
                "Setup.exe 生成失败",
                95,
                100,
                1,
                1,
                1,
                1,
                packageResult.LogFilePath,
                $"setup bundle failed: {ex.Message}",
                isError: true);

            return packageResult with
            {
                Outcome = PackagerOutcome.Failure,
                PackagePath = options.OutputSetupPath,
                SummaryMessage = $"生成安装器失败：{ex.Message}"
            };
        }
    }

    private static PackagerOptions ClonePackageOptions(PackagerOptions options) =>
        new()
        {
            RuntimeDirectory = options.RuntimeDirectory,
            AssetsDirectory = options.AssetsDirectory,
            RequirementsDirectory = options.RequirementsDirectory,
            VersionDirectory = options.VersionDirectory,
            ProductName = options.ProductName,
            PackageId = options.PackageId,
            Version = options.Version,
            Publisher = options.Publisher,
            Architecture = options.Architecture,
            RuntimeType = options.RuntimeType,
            RequiresAdmin = options.RequiresAdmin,
            DefaultInstallDirectory = options.DefaultInstallDirectory,
            OutputPackagePath = options.OutputPackagePath,
            EntryPoints = options.EntryPoints?.ToList(),
            EnvironmentVariables = options.EnvironmentVariables?.ToList()
        };

    private static string ResolveIntermediatePackagePath(string requestedPackagePath, string outputSetupPath)
    {
        if (!string.IsNullOrWhiteSpace(requestedPackagePath) &&
            string.Equals(Path.GetExtension(requestedPackagePath), ".vrp", StringComparison.OrdinalIgnoreCase))
        {
            return requestedPackagePath;
        }

        var outputDirectory = Path.GetDirectoryName(outputSetupPath);
        var outputFileName = Path.GetFileNameWithoutExtension(outputSetupPath);
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            outputDirectory = Environment.CurrentDirectory;
        }

        return Path.Combine(outputDirectory, outputFileName + ".vrp");
    }

    private static void ReportProgress(
        IProgress<BuildProgressUpdate>? progress,
        string stageDisplayName,
        int overallPercent,
        int stagePercent,
        int processedItems,
        int totalItems,
        long processedBytes,
        long totalBytes,
        string logFilePath,
        string message,
        bool isError = false) =>
        progress?.Report(new BuildProgressUpdate(
            BuildStage.CreateSetupBundle,
            stageDisplayName,
            overallPercent,
            stagePercent,
            processedItems,
            totalItems,
            processedBytes,
            totalBytes,
            message,
            logFilePath,
            isError));

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }
}
