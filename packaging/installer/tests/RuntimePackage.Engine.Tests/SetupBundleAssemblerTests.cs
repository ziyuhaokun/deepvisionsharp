using RuntimePackage.Engine.Packaging;
using RuntimePackage.Engine.Services;
using Xunit;

namespace RuntimePackage.Engine.Tests;

public sealed class SetupBundleAssemblerTests
{
    [Fact]
    public async Task Assemble_ThenExtract_RoundTripsPayloadBytesAndSha256()
    {
        var root = Path.Combine(Path.GetTempPath(), "RuntimePackage.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var hostPath = Path.Combine(root, "host.exe");
            var packagePath = Path.Combine(root, "payload.vrp");
            var setupPath = Path.Combine(root, "setup.exe");

            await File.WriteAllBytesAsync(hostPath, Enumerable.Repeat((byte)0x41, 2048).ToArray());
            var payloadBytes = Enumerable.Range(0, 4096).Select(i => (byte)(i % 251)).ToArray();
            await File.WriteAllBytesAsync(packagePath, payloadBytes);
            var expectedSha = await ChecksumService.ComputeSha256Async(packagePath, CancellationToken.None);

            await SetupBundleAssembler.AssembleAsync(hostPath, packagePath, setupPath, CancellationToken.None);

            Assert.True(File.Exists(setupPath));
            Assert.True(new FileInfo(setupPath).Length > new FileInfo(hostPath).Length);

            var extracted = await new SetupBundleReader().ExtractPayloadAsync(setupPath, CancellationToken.None);
            try
            {
                var extractedBytes = await File.ReadAllBytesAsync(extracted.ExtractedPackagePath);
                Assert.Equal(payloadBytes, extractedBytes);

                var extractedSha = await ChecksumService.ComputeSha256Async(
                    extracted.ExtractedPackagePath,
                    CancellationToken.None);
                Assert.Equal(expectedSha, extractedSha);
            }
            finally
            {
                TempDirectoryCleanup.TryDeleteDirectory(extracted.ExtractedRoot);
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

    [Fact]
    public async Task AssembledSetup_HostPrefix_MatchesOriginalHost()
    {
        var root = Path.Combine(Path.GetTempPath(), "RuntimePackage.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var hostPath = Path.Combine(root, "host.exe");
            var packagePath = Path.Combine(root, "payload.vrp");
            var setupPath = Path.Combine(root, "setup.exe");
            var hostBytes = Enumerable.Repeat((byte)0x42, 1024).ToArray();
            await File.WriteAllBytesAsync(hostPath, hostBytes);
            await File.WriteAllBytesAsync(packagePath, Enumerable.Repeat((byte)0x7, 512).ToArray());

            await SetupBundleAssembler.AssembleAsync(hostPath, packagePath, setupPath, CancellationToken.None);

            await using var setup = new FileStream(setupPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var prefix = new byte[hostBytes.Length];
            await setup.ReadExactlyAsync(prefix);
            Assert.Equal(hostBytes, prefix);
            Assert.Equal(
                hostBytes.Length + 512 + SetupBundleFooterLength(),
                new FileInfo(setupPath).Length);
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

    private static int SetupBundleFooterLength()
    {
        // VRPSETUP + offset + length + sha256 hex
        return 8 + sizeof(long) + sizeof(long) + 64;
    }
}
