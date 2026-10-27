using RuntimePackage.Engine.Services;

namespace RuntimePackage.Engine.Installation;

public sealed class PackageValidator
{
    private const string EmbeddedPayloadFileName = "runtime.zip";

    public async Task<ValidationResult> ValidateAsync(PackageReadResult package, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(package);
        cancellationToken.ThrowIfCancellationRequested();

        var errors = new List<string>();

        if (package.Manifest.SchemaVersion != 1)
        {
            errors.Add("不支持的架构版本。");
        }

        ValidateRequiredFile(package.ExtractedRoot, "manifest.json", errors);
        ValidateRequiredFile(package.ExtractedRoot, "assets/license.txt", errors);
        ValidateRequiredFile(package.ExtractedRoot, "assets/logo.ico", errors);
        ValidateRequiredFile(package.ExtractedRoot, "metadata/version.txt", errors);

        foreach (var requirementPath in package.Manifest.Metadata.Requirements)
        {
            ValidateRequiredFile(package.ExtractedRoot, requirementPath, errors);
        }

        var logoPngPath = Path.Combine(package.ExtractedRoot, "assets", "logo.png");
        if (!File.Exists(logoPngPath))
        {
            // Optional UI asset for cleaner rendering; ico remains the compatibility baseline.
        }

        if (!string.Equals(package.Manifest.Payload.File, EmbeddedPayloadFileName, StringComparison.Ordinal))
        {
            errors.Add("payload.file 必须为 runtime.zip。");
        }

        var payloadPath = Path.Combine(package.ExtractedRoot, EmbeddedPayloadFileName);
        if (!File.Exists(payloadPath))
        {
            errors.Add("缺少 runtime.zip。");
        }
        else
        {
            var payloadSha256 = await ChecksumService.ComputeSha256Async(payloadPath, cancellationToken);
            if (!string.Equals(payloadSha256, package.Manifest.Payload.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("runtime.zip 校验和不匹配。");
            }
        }

        return new ValidationResult(errors.Count == 0, errors);
    }

    private static void ValidateRequiredFile(string extractedRoot, string relativePath, List<string> errors)
    {
        var path = Path.Combine(extractedRoot, relativePath);
        if (!File.Exists(path))
        {
            errors.Add($"缺少文件：{relativePath}。");
        }
    }
}
