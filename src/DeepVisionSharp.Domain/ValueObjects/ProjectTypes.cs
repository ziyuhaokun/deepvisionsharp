namespace DeepVisionSharp.Domain.ValueObjects;

/// <summary>
/// Product project types. 1.0 exposes image classification only; any other stored
/// value is treated as unsupported data rather than a read-only historical type.
/// </summary>
public static class ProjectTypes
{
    public const string Classification = "Classification";

    /// <summary>Historical fallback for rows with an empty type column.</summary>
    public const string Default = Classification;

    public static bool IsValid(string? type) =>
        !string.IsNullOrWhiteSpace(type)
        && string.Equals(type.Trim(), Classification, StringComparison.OrdinalIgnoreCase);

    public static string Normalize(string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            throw new ArgumentException("项目类型不能为空。", nameof(type));
        }

        if (IsValid(type))
        {
            return Classification;
        }

        throw new ArgumentException($"不支持的项目类型：{type.Trim()}。当前仅支持图像分类。", nameof(type));
    }

    public static string GetDisplayLabel(string? type) =>
        IsValid(type) ? "图像分类" : "其他类型";

    public static string GetDescription(string? type) =>
        IsValid(type) ? "对整张图像判定类别（如 OK/NG），仅图像级标注。" : string.Empty;

    /// <summary>
    /// Imported packages must carry a supported type. Historical types are no longer
    /// migrated; the import fails with the normalized type error.
    /// </summary>
    public static string MigrateImportedType(string? type) => Normalize(type);
}
