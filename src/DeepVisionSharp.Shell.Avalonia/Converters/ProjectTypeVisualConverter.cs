using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using YuUI;

namespace DeepVisionSharp.Shell.Avalonia.Converters;

/// <summary>项目类型视觉：图标 + 主色 + 淡底色。</summary>
internal sealed record ProjectTypeVisual(Geometry Icon, Color Color, Color TintColor);

/// <summary>与 deepvision 的 ProjectTypeVisuals 逐值一致（色值不动，图标换成 yu-ui 图标集）。</summary>
internal static class ProjectTypeVisuals
{
    // ProjectTypes 的类型键（Domain 的 ProjectTypes 常量，本波不引用 Domain）
    private const string ObjectDetection = "ObjectDetection";

    private static readonly ProjectTypeVisual DefaultVisual = new(
        IvIcons.Grid,
        Color.FromRgb(0x5C, 0x8F, 0xCB),
        Color.FromArgb(0x26, 0x5C, 0x8F, 0xCB));

    private static readonly IReadOnlyDictionary<string, ProjectTypeVisual> ByKey =
        new Dictionary<string, ProjectTypeVisual>(StringComparer.OrdinalIgnoreCase)
        {
            [ObjectDetection] = DefaultVisual,
            ["Classification"] = new(IvIcons.Image, Color.FromRgb(0x8A, 0x79, 0xD6), Color.FromArgb(0x26, 0x8A, 0x79, 0xD6)),
            ["InstanceSegmentation"] = new(IvIcons.Edit, Color.FromRgb(0xCE, 0x8A, 0x46), Color.FromArgb(0x26, 0xCE, 0x8A, 0x46)),
            ["SemanticSegmentation"] = new(IvIcons.Edit, Color.FromRgb(0x4F, 0xA0, 0xB0), Color.FromArgb(0x26, 0x4F, 0xA0, 0xB0)),
            ["VariationClassification"] = new(IvIcons.Warning, Color.FromRgb(0xD9, 0x6D, 0x63), Color.FromArgb(0x26, 0xD9, 0x6D, 0x63)),
            ["UnsupervisedAnomaly"] = new(IvIcons.Sparkles, Color.FromRgb(0x73, 0x7E, 0xD8), Color.FromArgb(0x26, 0x73, 0x7E, 0xD8)),
            ["TextRecognition"] = new(IvIcons.Document, Color.FromRgb(0x4E, 0x9C, 0x84), Color.FromArgb(0x26, 0x4E, 0x9C, 0x84)),
        };

    public static ProjectTypeVisual Get(string? typeKey)
        => ByKey.TryGetValue(typeKey ?? string.Empty, out var visual) ? visual : DefaultVisual;
}

public sealed class ProjectTypeVisualConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var visual = ProjectTypeVisuals.Get(value as string);
        return (parameter as string) switch
        {
            "Icon" => visual.Icon,
            "Foreground" => new SolidColorBrush(visual.Color),
            "Badge" => new SolidColorBrush(visual.Color),
            "Tint" => new SolidColorBrush(visual.TintColor),
            _ => throw new ArgumentOutOfRangeException(
                nameof(parameter),
                parameter,
                "Unknown ProjectTypeVisualConverter parameter; expected Icon/Foreground/Badge/Tint."),
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
