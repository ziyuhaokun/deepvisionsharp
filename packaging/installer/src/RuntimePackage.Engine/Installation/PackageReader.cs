using RuntimePackage.Engine.Serialization;
using RuntimePackage.Engine.Services;

namespace RuntimePackage.Engine.Installation;

public sealed class PackageReader
{
    public async Task<PackageReadResult> OpenAsync(string packagePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        cancellationToken.ThrowIfCancellationRequested();

        var extractedRoot = Path.Combine(Path.GetTempPath(), "RuntimePackage.Installer", Guid.NewGuid().ToString("N"));
        await PackageArchiveService.ExtractToDirectoryAsync(packagePath, extractedRoot, cancellationToken);

        var manifestPath = Path.Combine(extractedRoot, "manifest.json");
        var manifestJson = await File.ReadAllTextAsync(manifestPath, cancellationToken);

        return new PackageReadResult(packagePath, extractedRoot, ManifestSerializer.Deserialize(manifestJson));
    }
}
