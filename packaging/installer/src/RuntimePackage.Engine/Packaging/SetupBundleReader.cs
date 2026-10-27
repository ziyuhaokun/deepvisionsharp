using RuntimePackage.Engine.Services;

namespace RuntimePackage.Engine.Packaging;

public sealed record SetupBundlePayload(string SourceExecutablePath, string ExtractedPackagePath, string ExtractedRoot);

public sealed class SetupBundleReader
{
    public async Task<SetupBundlePayload> ExtractPayloadAsync(
        string setupExecutablePath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(setupExecutablePath);
        cancellationToken.ThrowIfCancellationRequested();

        var executableInfo = new FileInfo(setupExecutablePath);
        if (!executableInfo.Exists)
        {
            throw new FileNotFoundException("Setup executable not found.", setupExecutablePath);
        }

        if (executableInfo.Length <= SetupBundleFooter.Length)
        {
            throw new InvalidOperationException("setup bundle 不包含 payload。");
        }

        SetupBundleFooter footer;
        await using (var source = new FileStream(setupExecutablePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            source.Seek(-SetupBundleFooter.Length, SeekOrigin.End);
            var footerBytes = new byte[SetupBundleFooter.Length];
            await source.ReadExactlyAsync(footerBytes, cancellationToken);
            footer = SetupBundleFooter.FromBytes(footerBytes);

            if (footer.PayloadOffset + footer.PayloadLength + SetupBundleFooter.Length != source.Length)
            {
                throw new InvalidOperationException("setup bundle payload 长度无效。");
            }
        }

        var extractedRoot = Path.Combine(Path.GetTempPath(), "RuntimePackage.Setup", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(extractedRoot);
        var extractedPackagePath = Path.Combine(extractedRoot, "payload.vrp");

        try
        {
            await ExtractPayloadRangeAsync(
                setupExecutablePath,
                extractedPackagePath,
                footer.PayloadOffset,
                footer.PayloadLength,
                cancellationToken);

            var sha256 = await ChecksumService.ComputeSha256Async(extractedPackagePath, cancellationToken);
            if (!string.Equals(sha256, footer.PayloadSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("setup bundle payload 校验和不匹配。");
            }

            return new SetupBundlePayload(setupExecutablePath, extractedPackagePath, extractedRoot);
        }
        catch
        {
            TempDirectoryCleanup.TryDeleteDirectory(extractedRoot);
            throw;
        }
    }

    private static async Task ExtractPayloadRangeAsync(
        string setupExecutablePath,
        string destinationPath,
        long offset,
        long length,
        CancellationToken cancellationToken)
    {
        await using var source = new FileStream(setupExecutablePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using var destination = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);

        source.Seek(offset, SeekOrigin.Begin);
        var buffer = new byte[131072];
        long remaining = length;

        while (remaining > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bytesToRead = (int)Math.Min(buffer.Length, remaining);
            var read = await source.ReadAsync(buffer.AsMemory(0, bytesToRead), cancellationToken);
            if (read == 0)
            {
                throw new EndOfStreamException("setup bundle payload 读取不完整。");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            remaining -= read;
        }
    }

}
