using RuntimePackage.Engine.Models;
using RuntimePackage.Engine.Packaging;
using RuntimePackage.Engine.Packaging.Build;

try
{
    var arguments = ParseArguments(args);
    var outputSetupPath = GetRequired(arguments, "output");
    var runtimeType = GetOptional(arguments, "runtime-type") ?? new PackagerOptions().RuntimeType;
    var entryPointRelativePath = GetOptional(arguments, "entry-point");

    List<EntryPointDescriptor>? entryPoints = null;
    if (!string.IsNullOrWhiteSpace(entryPointRelativePath))
    {
        var entryFileName = Path.GetFileName(entryPointRelativePath);
        var entryId = Path.GetFileNameWithoutExtension(entryFileName);
        entryPoints =
        [
            new EntryPointDescriptor(
                string.IsNullOrWhiteSpace(entryId) ? "app" : entryId,
                string.IsNullOrWhiteSpace(entryId) ? "Application" : entryId,
                entryPointRelativePath.Replace('/', '\\'),
                string.Empty)
        ];
    }

    var packageOptions = new PackagerOptions
    {
        RuntimeDirectory = GetRequired(arguments, "runtime"),
        AssetsDirectory = GetRequired(arguments, "assets"),
        RequirementsDirectory = GetOptional(arguments, "requirements") ?? string.Empty,
        VersionDirectory = GetRequired(arguments, "version-dir"),
        Version = GetRequired(arguments, "version"),
        RuntimeType = runtimeType,
        PackageId = GetOptional(arguments, "package-id") ?? new PackagerOptions().PackageId,
        ProductName = GetOptional(arguments, "product-name") ?? new PackagerOptions().ProductName,
        Publisher = GetOptional(arguments, "publisher") ?? new PackagerOptions().Publisher,
        DefaultInstallDirectory = GetOptional(arguments, "default-install-dir")
            ?? new PackagerOptions().DefaultInstallDirectory,
        EntryPoints = entryPoints,
        OutputPackagePath = Path.ChangeExtension(outputSetupPath, ".vrp")
    };

    var keepIntermediate = GetOptional(arguments, "keep-intermediate") is { } keep
        && (string.Equals(keep, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(keep, "true", StringComparison.OrdinalIgnoreCase));

    var progress = new ConsoleBuildProgress();
    var result = await new SetupBundleBuilder().BuildAsync(
        new SetupBundleOptions
        {
            PackageOptions = packageOptions,
            SetupHostExecutablePath = GetRequired(arguments, "setup-host"),
            BootstrapperProjectPath = GetOptional(arguments, "bootstrapper-project") ?? string.Empty,
            OutputSetupPath = outputSetupPath,
            KeepIntermediatePackage = keepIntermediate
        },
        progress,
        CancellationToken.None);

    Console.WriteLine(result.SummaryMessage);
    return result.Success ? 0 : 1;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

static Dictionary<string, string> ParseArguments(string[] args)
{
    if (args.Length == 0 || args.Length % 2 != 0)
    {
        throw new ArgumentException("Arguments must be provided as --name value pairs.");
    }

    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var index = 0; index < args.Length; index += 2)
    {
        var name = args[index];
        if (!name.StartsWith("--", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(args[index + 1]))
        {
            throw new ArgumentException($"Invalid argument pair: {name}");
        }

        result[name[2..]] = args[index + 1];
    }

    return result;
}

static string GetRequired(IReadOnlyDictionary<string, string> arguments, string name) =>
    arguments.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
        ? value
        : throw new ArgumentException($"Missing required argument: --{name}");

static string? GetOptional(IReadOnlyDictionary<string, string> arguments, string name) =>
    arguments.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
        ? value
        : null;

internal sealed class ConsoleBuildProgress : IProgress<BuildProgressUpdate>
{
    private int _lastOverallPercent = -1;

    public void Report(BuildProgressUpdate update)
    {
        if (!update.IsError && update.OverallPercent == _lastOverallPercent)
        {
            return;
        }

        _lastOverallPercent = update.OverallPercent;
        Console.WriteLine($"[{update.OverallPercent,3}%] {update.Message}");
    }
}
