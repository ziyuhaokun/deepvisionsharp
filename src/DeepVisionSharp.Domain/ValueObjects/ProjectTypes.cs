namespace DeepVisionSharp.Domain.ValueObjects;

/// <summary>
/// Product project types: image classification and object detection. Any other stored
/// value is treated as unsupported data rather than a read-only historical type.
/// </summary>
public static class ProjectTypes
{
    public const string Classification = "Classification";

    public const string Detection = "Detection";

    /// <summary>Historical fallback for rows with an empty type column.</summary>
    public const string Default = Classification;

    public static bool IsValid(string? type) =>
        !string.IsNullOrWhiteSpace(type)
        && (string.Equals(type.Trim(), Classification, StringComparison.OrdinalIgnoreCase)
            || string.Equals(type.Trim(), Detection, StringComparison.OrdinalIgnoreCase));

    public static string Normalize(string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            throw new ArgumentException("项目类型不能为空。", nameof(type));
        }

        if (string.Equals(type.Trim(), Classification, StringComparison.OrdinalIgnoreCase))
        {
            return Classification;
        }

        if (string.Equals(type.Trim(), Detection, StringComparison.OrdinalIgnoreCase))
        {
            return Detection;
        }

        throw new ArgumentException($"不支持的项目类型：{type.Trim()}。当前支持图像分类与目标检测。", nameof(type));
    }

    public static bool IsDetection(string? type) =>
        !string.IsNullOrWhiteSpace(type)
        && string.Equals(type.Trim(), Detection, StringComparison.OrdinalIgnoreCase);

    public static string GetDisplayLabel(string? type)
    {
        if (IsDetection(type))
        {
            return "目标检测";
        }

        return IsValid(type) ? "图像分类" : "其他类型";
    }

    public static string GetDescription(string? type)
    {
        if (IsDetection(type))
        {
            return "在图上框出目标并分类（如缺陷框选），支持多框与负样本。";
        }

        return IsValid(type) ? "对整张图像判定类别（如 OK/NG），仅图像级标注。" : string.Empty;
    }

    /// <summary>
    /// Imported packages must carry a supported type. Historical types are no longer
    /// migrated; the import fails with the normalized type error.
    /// </summary>
    public static string MigrateImportedType(string? type) => Normalize(type);
}
