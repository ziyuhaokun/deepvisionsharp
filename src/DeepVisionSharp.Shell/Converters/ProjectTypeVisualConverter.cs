using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using DeepVisionSharp.Domain.ValueObjects;
using Wpf.Ui.Controls;

namespace DeepVisionSharp.Shell.Converters;

/// <summary>
/// Per project-type visual identity (icon glyph + accent color + tint).
/// Single source of truth consumed by the dashboard card styles via
/// <see cref="ProjectTypeVisualConverter"/> — no more per-type DataTriggers.
/// </summary>
public sealed record ProjectTypeVisual(SymbolRegular Icon, Color Color, Color TintColor);

public static class ProjectTypeVisuals
{
    private static readonly ProjectTypeVisual DefaultVisual = new(
        SymbolRegular.Image24,
        Color.FromRgb(0x8B, 0x5C, 0xF6),
        Color.FromArgb(0x26, 0x8B, 0x5C, 0xF6));

    private static readonly ProjectTypeVisual DetectionVisual = new(
        SymbolRegular.Crop24,
        Color.FromRgb(0xF5, 0x9E, 0x0B),
        Color.FromArgb(0x26, 0xF5, 0x9E, 0x0B));

    private static readonly IReadOnlyDictionary<string, ProjectTypeVisual> ByKey =
        new Dictionary<string, ProjectTypeVisual>(StringComparer.OrdinalIgnoreCase)
        {
            [ProjectTypes.Classification] = DefaultVisual,
            [ProjectTypes.Detection] = DetectionVisual
        };

    public static ProjectTypeVisual Get(string? typeKey)
        => ByKey.TryGetValue(typeKey ?? string.Empty, out var visual) ? visual : DefaultVisual;
}

/// <summary>
/// Resolves a project type key to its visual identity.
/// ConverterParameter selects the output:
/// <c>Icon</c> → <see cref="SymbolRegular"/>, <c>Foreground</c> → accent brush,
/// <c>Badge</c> → accent brush, <c>Tint</c> → translucent accent brush.
/// </summary>
public sealed class ProjectTypeVisualConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var visual = ProjectTypeVisuals.Get(value as string);
        return (parameter as string) switch
        {
            "Icon" => visual.Icon,
            "Foreground" => CreateBrush(visual.Color),
            "Badge" => CreateBrush(visual.Color),
            "Tint" => CreateBrush(visual.TintColor),
            _ => throw new ArgumentOutOfRangeException(
                nameof(parameter),
                parameter,
                "Unknown ProjectTypeVisualConverter parameter; expected Icon/Foreground/Badge/Tint.")
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static SolidColorBrush CreateBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}