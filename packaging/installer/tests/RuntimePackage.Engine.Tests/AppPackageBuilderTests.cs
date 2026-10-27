using RuntimePackage.Engine.Installation;
using RuntimePackage.Engine.Models;
using RuntimePackage.Engine.Packaging;
using Xunit;

namespace RuntimePackage.Engine.Tests;

public sealed class AppPackageBuilderTests
{
    [Fact]
    public async Task BuildAppPackage_DoesNotRequireRequirementsAndUsesConfiguredEntryPoint()
    {
        var root = Path.Combine(Path.GetTempPath(), "RuntimePackage.Tests", Guid.NewGuid().ToString("N"));
        var payloadDir = Path.Combine(root, "payload");
        var assetsDir = Path.Combine(root, "assets");
        var versionDir = Path.Combine(root, "version");
        var outputPath = Path.Combine(root, "app.vrp");

        Directory.CreateDirectory(payloadDir);
        Directory.CreateDirectory(assetsDir);
        Directory.CreateDirectory(versionDir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(payloadDir, "DeepVisionSharp.exe"), "fake-app");
            await File.WriteAllTextAsync(Path.Combine(assetsDir, "license.txt"), "license");
            await File.WriteAllBytesAsync(Path.Combine(assetsDir, "logo.ico"), [0, 1, 2, 3]);
            await File.WriteAllTextAsync(Path.Combine(versionDir, "version.txt"), "0.0.1");

            var result = await new RuntimePackageBuilder().BuildAsync(
                new PackagerOptions
                {
                    RuntimeDirectory = payloadDir,
                    AssetsDirectory = assetsDir,
                    VersionDirectory = versionDir,
                    RuntimeType = "app",
                    PackageId = "vision.deepvisionsharp",
                    ProductName = "DeepVisionSharp",
                    Version = "0.0.1",
                    DefaultInstallDirectory = "%LocalAppData%\\DeepVisionSharp",
                    EntryPoints =
                    [
                        new EntryPointDescriptor(
                            "DeepVisionSharp",
                            "DeepVisionSharp",
                            "DeepVisionSharp.exe",
                            string.Empty)
                    ],
                    EnvironmentVariables = [],
                    OutputPackagePath = outputPath
                },
                progress: null,
                CancellationToken.None);

            Assert.True(result.Success, result.SummaryMessage);
            Assert.True(File.Exists(outputPath));

            var package = await new PackageReader().OpenAsync(outputPath, CancellationToken.None);
            try
            {
                Assert.Equal("app", package.Manifest.RuntimeType);
                Assert.Equal("vision.deepvisionsharp", package.Manifest.PackageId);
                Assert.Empty(package.Manifest.Metadata.Requirements);
                Assert.Empty(package.Manifest.EnvironmentVariables);
                Assert.Equal(
                    "DeepVisionSharp.exe",
                    package.Manifest.EntryPoints.Single().RelativePath);
            }
            finally
            {
                RuntimePackage.Engine.Services.TempDirectoryCleanup.TryDeleteDirectory(package.ExtractedRoot);
            }
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
            }
        }
    }
}
