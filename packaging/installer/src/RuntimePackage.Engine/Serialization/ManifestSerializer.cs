using RuntimePackage.Engine.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RuntimePackage.Engine.Serialization;

public static class ManifestSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Serialize(PackageManifest manifest) =>
        JsonSerializer.Serialize(Validate(manifest), Options);

    public static PackageManifest Deserialize(string json) =>
        Validate(JsonSerializer.Deserialize<PackageManifest>(json, Options)
            ?? throw new InvalidOperationException("清单反序列化结果为空。"));

    private static PackageManifest Validate(PackageManifest manifest)
    {
        var errors = new List<string>();

        if (manifest.SchemaVersion <= 0)
        {
            errors.Add("schemaVersion 必须大于 0。");
        }

        if (string.IsNullOrWhiteSpace(manifest.PackageId))
        {
            errors.Add("packageId 为必填项。");
        }

        if (string.IsNullOrWhiteSpace(manifest.ProductName))
        {
            errors.Add("productName 为必填项。");
        }

        if (string.IsNullOrWhiteSpace(manifest.Version))
        {
            errors.Add("version 为必填项。");
        }

        if (string.IsNullOrWhiteSpace(manifest.Publisher))
        {
            errors.Add("publisher 为必填项。");
        }

        if (string.IsNullOrWhiteSpace(manifest.RuntimeType))
        {
            errors.Add("runtimeType 为必填项。");
        }

        if (string.IsNullOrWhiteSpace(manifest.Architecture))
        {
            errors.Add("architecture 为必填项。");
        }

        if (manifest.Payload is null || string.IsNullOrWhiteSpace(manifest.Payload.File))
        {
            errors.Add("payload.file 为必填项。");
        }

        if (manifest.Payload is null || string.IsNullOrWhiteSpace(manifest.Payload.Sha256))
        {
            errors.Add("payload.sha256 为必填项。");
        }

        if (manifest.Payload is null || manifest.Payload.SizeBytes <= 0)
        {
            errors.Add("payload.sizeBytes 必须大于 0。");
        }

        if (manifest.CreatedAtUtc == default)
        {
            errors.Add("createdAt 为必填项。");
        }
        else if (manifest.CreatedAtUtc.Kind != DateTimeKind.Utc)
        {
            errors.Add("createdAt 必须为 UTC 时间。");
        }

        if (manifest.Assets is null || string.IsNullOrWhiteSpace(manifest.Assets.License))
        {
            errors.Add("assets.license 为必填项。");
        }

        if (manifest.Assets is null || string.IsNullOrWhiteSpace(manifest.Assets.Logo))
        {
            errors.Add("assets.logo 为必填项。");
        }

        if (manifest.Metadata is null || string.IsNullOrWhiteSpace(manifest.Metadata.VersionFile))
        {
            errors.Add("metadata.versionFile 为必填项。");
        }

        var requiresRequirements = string.Equals(manifest.RuntimeType, "python", StringComparison.OrdinalIgnoreCase);
        if (requiresRequirements &&
            (manifest.Metadata is null || manifest.Metadata.Requirements is null || manifest.Metadata.Requirements.Count == 0))
        {
            errors.Add("metadata.requirements 至少需要一个依赖路径。");
        }

        if (manifest.Install is null || string.IsNullOrWhiteSpace(manifest.Install.DefaultDirectory))
        {
            errors.Add("install.defaultDirectory 为必填项。");
        }

        if (manifest.Install is null || string.IsNullOrWhiteSpace(manifest.Install.UninstallDisplayName))
        {
            errors.Add("install.uninstallDisplayName 为必填项。");
        }

        if (manifest.EnvironmentVariables is null)
        {
            errors.Add("environmentVariables 不能为空。");
        }
        else if (manifest.EnvironmentVariables.Any(variable =>
            string.IsNullOrWhiteSpace(variable.Name) ||
            string.IsNullOrWhiteSpace(variable.Value) ||
            string.IsNullOrWhiteSpace(variable.Target)))
        {
            errors.Add("environmentVariables 中的 name、value 和 target 不能为空。");
        }
        else if (manifest.EnvironmentVariables.Any(variable =>
            !Enum.TryParse<EnvironmentVariableTarget>(variable.Target, ignoreCase: true, out var parsed) ||
            parsed is not (EnvironmentVariableTarget.User or EnvironmentVariableTarget.Machine or EnvironmentVariableTarget.Process)))
        {
            errors.Add("environmentVariables 中的 target 仅支持 User、Machine 或 Process。");
        }

        if (manifest.EntryPoints is null || manifest.EntryPoints.Count == 0)
        {
            errors.Add("至少需要一个入口点。");
        }
        else if (manifest.EntryPoints.Any(entryPoint =>
            string.IsNullOrWhiteSpace(entryPoint.Id) ||
            string.IsNullOrWhiteSpace(entryPoint.Label) ||
            string.IsNullOrWhiteSpace(entryPoint.RelativePath)))
        {
            errors.Add("entryPoints 中的 id、label 和 relativePath 不能为空。");
        }

        if (errors.Count > 0)
        {
            throw new InvalidOperationException("清单无效：" + string.Join(" ", errors));
        }

        return manifest;
    }
}
