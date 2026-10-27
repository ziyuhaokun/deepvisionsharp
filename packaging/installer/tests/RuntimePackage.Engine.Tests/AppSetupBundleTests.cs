using RuntimePackage.Engine.Installation;
using RuntimePackage.Engine.Models;
using RuntimePackage.Engine.Packaging;
using RuntimePackage.Engine.Services;
using Xunit;

namespace RuntimePackage.Engine.Tests;

public sealed class AppSetupBundleTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BuildAppSetup_RoundTripsManifestAndPayload(bool keepIntermediate)
    {
        var root = Path.Combine(Path.GetTempPath(), "RuntimePackage.Tests", Guid.NewGuid().ToString("N"));
        var payload = Path.Combine(root, "application");
        var assets = Path.Combine(root, "assets");
        var version = Path.Combine(root, "version");
        Directory.CreateDirectory(payload);
        Directory.CreateDirectory(assets);
        Directory.CreateDirectory(version);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(payload, "DeepVisionSharp.exe"), "application-payload");
            await File.WriteAllTextAsync(Path.Combine(assets, "license.txt"), "license");
            await File.WriteAllBytesAsync(Path.Combine(assets, "logo.ico"), [0, 1, 2, 3]);
            await File.WriteAllTextAsync(Path.Combine(version, "version.txt"), "0.0.1");
            var host = Path.Combine(root, "host.exe");
            await File.WriteAllBytesAsync(host, new byte[1024]);
            var setup = Path.Combine(root, "output", "DeepVisionSharp.exe");
            var vrp = Path.ChangeExtension(setup, ".vrp");
            var result = await new SetupBundleBuilder().BuildAsync(new SetupBundleOptions
            {
                SetupHostExecutablePath = host,
                OutputSetupPath = setup,
                KeepIntermediatePackage = keepIntermediate,
                PackageOptions = new PackagerOptions
                {
                    RuntimeDirectory = payload,
                    AssetsDirectory = assets,
                    VersionDirectory = version,
                    Version = "0.0.1",
                    RuntimeType = "app",
                    PackageId = "vision.deepvisionsharp",
                    ProductName = "DeepVisionSharp",
                    DefaultInstallDirectory = "%LocalAppData%\\DeepVisionSharp",
                    EntryPoints = [new EntryPointDescriptor("deepvisionsharp", "DeepVisionSharp", "DeepVisionSharp.exe", "")],
                    OutputPackagePath = vrp
                }
            }, null, CancellationToken.None);

            Assert.True(result.Success, result.SummaryMessage);
            Assert.Equal(setup, result.PackagePath);
            Assert.Equal(keepIntermediate, File.Exists(vrp));
            var bundle = await new SetupBundleReader().ExtractPayloadAsync(setup, CancellationToken.None);
            try
            {
                var package = await new PackageReader().OpenAsync(bundle.ExtractedPackagePath, CancellationToken.None);
                try
                {
                    Assert.Equal("vision.deepvisionsharp", package.Manifest.PackageId);
                    Assert.Equal("app", package.Manifest.RuntimeType);
                    Assert.Equal("0.0.1", package.Manifest.Version);
                    Assert.Equal("%LocalAppData%\\DeepVisionSharp", package.Manifest.Install.DefaultDirectory);
                    Assert.False(package.Manifest.RequiresAdmin);
                    Assert.Empty(package.Manifest.EnvironmentVariables);
                    Assert.Empty(package.Manifest.Metadata.Requirements);
                    Assert.Equal("DeepVisionSharp.exe", package.Manifest.EntryPoints.Single().RelativePath);
                    var archive = Path.Combine(package.ExtractedRoot, package.Manifest.Payload.File);
                    using var zip = System.IO.Compression.ZipFile.OpenRead(archive);
                    using var reader = new StreamReader(zip.GetEntry("DeepVisionSharp.exe")!.Open());
                    Assert.Equal("application-payload", await reader.ReadToEndAsync());
                }
                finally { TempDirectoryCleanup.TryDeleteDirectory(package.ExtractedRoot); }
            }
            finally { TempDirectoryCleanup.TryDeleteDirectory(bundle.ExtractedRoot); }
        }
        finally { TempDirectoryCleanup.TryDeleteDirectory(root); }
    }
}
