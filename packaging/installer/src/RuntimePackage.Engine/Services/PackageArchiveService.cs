using System.IO.Compression;

namespace RuntimePackage.Engine.Services;

public static class PackageArchiveService
{
    public static Task CreateFromDirectoryAsync(string sourceDirectory, string packagePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.Run(() =>
        {
            var packageDirectory = Path.GetDirectoryName(packagePath);
            if (!string.IsNullOrWhiteSpace(packageDirectory))
            {
                Directory.CreateDirectory(packageDirectory);
            }

            var tempPackagePath = Path.Combine(
                Path.GetTempPath(),
                $"{Path.GetFileName(packagePath)}.{Guid.NewGuid():N}.tmp");

            try
            {
                ZipFile.CreateFromDirectory(sourceDirectory, tempPackagePath, CompressionLevel.Fastest, includeBaseDirectory: false);
                File.Move(tempPackagePath, packagePath, overwrite: true);
            }
            finally
            {
                if (File.Exists(tempPackagePath))
                {
                    File.Delete(tempPackagePath);
                }
            }
        });
    }

    public static Task ExtractToDirectoryAsync(string packagePath, string destinationDirectory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.Run(() =>
        {
            if (File.Exists(destinationDirectory))
            {
                File.Delete(destinationDirectory);
            }

            Directory.CreateDirectory(destinationDirectory);
            ZipFile.ExtractToDirectory(packagePath, destinationDirectory, overwriteFiles: true);
        });
    }
}
