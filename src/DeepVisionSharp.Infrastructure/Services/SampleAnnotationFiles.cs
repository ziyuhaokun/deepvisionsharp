using System.Text.Json;
using FreeSql;
using DeepVisionSharp.Infrastructure.Persistence;

namespace DeepVisionSharp.Infrastructure.Services;

/// <summary>One sidecar per full image filename; legacy stem-only files require an unambiguous owner.</summary>
internal static class SampleAnnotationFiles
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff", ".webp" };

    internal static string GetPath(string imagePath) => imagePath + ".json";

    internal static async Task PreserveLegacyCollisionAsync(IFreeSql db, Guid galleryId, string imagePath, CancellationToken token)
    {
        if (!HasStemCollision(imagePath) || !File.Exists(Path.ChangeExtension(imagePath, ".json"))) return;
        // A move/delete can turn an ambiguous stem into a unique one. Materialize all surviving
        // owners before changing that evidence, otherwise the remaining image could inherit stale JSON.
        foreach (var sibling in Directory.EnumerateFiles(Path.GetDirectoryName(imagePath)!)
            .Where(path => ImageExtensions.Contains(Path.GetExtension(path))
                && string.Equals(Path.GetFileNameWithoutExtension(path), Path.GetFileNameWithoutExtension(imagePath), StringComparison.OrdinalIgnoreCase)))
        {
            if (File.Exists(GetPath(sibling))) continue;
            var name = Path.GetFileName(sibling);
            var row = await db.Select<SqlAnnotation>().Where(item => item.GalleryId == galleryId && item.FileName == name)
                .FirstAsync(token).ConfigureAwait(false);
            var content = await ReadAsync(sibling, row?.DocumentJson, token).ConfigureAwait(false);
            // An empty document records that this image did not own the shared legacy annotation.
            var json = content.Json ?? "{}";
            var staged = GetPath(sibling) + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllTextAsync(staged, json, token).ConfigureAwait(false);
                File.Move(staged, GetPath(sibling), overwrite: false);
            }
            finally { if (File.Exists(staged)) File.Delete(staged); }
        }
    }

    internal static bool HasStemCollision(string imagePath)
    {
        var directory = Path.GetDirectoryName(imagePath);
        return Directory.Exists(directory) && Directory.EnumerateFiles(directory!, Path.GetFileNameWithoutExtension(imagePath) + ".*")
            .Where(path => ImageExtensions.Contains(Path.GetExtension(path))
                && string.Equals(Path.GetFileNameWithoutExtension(path), Path.GetFileNameWithoutExtension(imagePath),
                    StringComparison.OrdinalIgnoreCase)).Take(2).Count() > 1;
    }

    internal static async Task<string?> FindOwnedLegacyAsync(string imagePath, CancellationToken token)
    {
        var legacy = Path.ChangeExtension(imagePath, ".json");
        if (!File.Exists(legacy)) return null;
        // Preserve the old contract for unique stems, including imports renamed without updating imagePath.
        if (!HasStemCollision(imagePath)) return legacy;
        var json = await File.ReadAllTextAsync(legacy, token).ConfigureAwait(false);
        using var document = JsonDocument.Parse(json);
        var owner = document.RootElement.TryGetProperty("imagePath", out var value)
            && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        if (!string.IsNullOrWhiteSpace(owner))
        {
            // Labelme may contain a path from another OS.
            return string.Equals(owner.Replace('\\', '/').Split('/')[^1], Path.GetFileName(imagePath),
                StringComparison.OrdinalIgnoreCase) ? legacy : null;
        }
        return null;
    }

    internal static async Task<Content> ReadAsync(string? imagePath, string? databaseJson, CancellationToken token)
    {
        if (imagePath is null) return new(databaseJson, null);
        var canonical = GetPath(imagePath);
        if (File.Exists(canonical))
            return new(await File.ReadAllTextAsync(canonical, token).ConfigureAwait(false), canonical);

        // Old saves may have overwritten a shared stem-only JSON. Exact-filename database rows survive.
        if (HasStemCollision(imagePath) && !string.IsNullOrWhiteSpace(databaseJson))
            return new(databaseJson, null);

        var legacy = await FindOwnedLegacyAsync(imagePath, token).ConfigureAwait(false);
        if (legacy is not null)
            return new(await File.ReadAllTextAsync(legacy, token).ConfigureAwait(false), legacy);
        var legacyPath = Path.ChangeExtension(imagePath, ".json");
        if (string.IsNullOrWhiteSpace(databaseJson) && File.Exists(legacyPath) && HasStemCollision(imagePath))
        {
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(legacyPath, token).ConfigureAwait(false));
            if (!document.RootElement.TryGetProperty("imagePath", out var owner)
                || owner.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(owner.GetString()))
                throw new InvalidDataException($"旧标注归属不明确：{Path.GetFileName(imagePath)}。请为每张图片提供独立的完整文件名 .json 标注。");
        }
        return new(databaseJson, null);
    }

    internal sealed record Content(string? Json, string? FilePath);
}
