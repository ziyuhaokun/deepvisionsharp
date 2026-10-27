using RuntimePackage.Engine.Services;

namespace RuntimePackage.Engine.Packaging;

public static class SetupBundleAssembler
{
    public static async Task AssembleAsync(
        string setupHostExecutablePath,
        string packagePath,
        string outputSetupPath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(setupHostExecutablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputSetupPath);
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(setupHostExecutablePath))
        {
            throw new FileNotFoundException("SetupHost executable not found.", setupHostExecutablePath);
        }

        if (!File.Exists(packagePath))
        {
            throw new FileNotFoundException("Package payload not found.", packagePath);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputSetupPath)!);

        var tempOutputPath = Path.Combine(
            Path.GetDirectoryName(outputSetupPath)!,
            $".{Path.GetFileName(outputSetupPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            File.Copy(setupHostExecutablePath, tempOutputPath, overwrite: true);

            long payloadOffset;
            long payloadLength;
            await using (var output = new FileStream(tempOutputPath, FileMode.Append, FileAccess.Write, FileShare.None))
            {
                payloadOffset = output.Position;
                await using (var payload = new FileStream(packagePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    await payload.CopyToAsync(output, cancellationToken);
                    payloadLength = payload.Length;
                }

                var sha256 = await ChecksumService.ComputeSha256Async(packagePath, cancellationToken);
                var footer = new SetupBundleFooter(payloadOffset, payloadLength, sha256);
                await output.WriteAsync(footer.ToBytes(), cancellationToken);
            }

            File.Move(tempOutputPath, outputSetupPath, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(tempOutputPath))
                {
                    File.Delete(tempOutputPath);
                }
            }
            catch
            {
            }
        }
    }
}
