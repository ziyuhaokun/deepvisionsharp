using System.Windows.Media;

namespace DeepVisionSharp.Shell.Rendering;

/// <summary>
/// Shared chart colors. Series hues stay fixed instead of following the theme brushes:
/// they must remain distinguishable on both light and dark surfaces. Theme dependence
/// enters through the text / grid / card brushes supplied by each render request.
/// </summary>
internal static class ChartPalette
{
    public static readonly Color Accent = Color.FromRgb(59, 130, 246);

    public static readonly Color Success = Color.FromRgb(34, 197, 94);

    public static readonly Color Warning = Color.FromRgb(245, 158, 11);

    public static readonly Color Critical = Color.FromRgb(239, 68, 68);

    public static readonly Color Miss = Color.FromRgb(248, 113, 113);

    public static readonly Color Neutral = Color.FromRgb(100, 116, 139);

    public static readonly Color Purple = Color.FromRgb(139, 92, 246);

    /// <summary>Stable hue order for labeled series (labels, galleries).</summary>
    public static readonly IReadOnlyList<Color> Series =
    [
        Accent,
        Color.FromRgb(16, 185, 129),
        Warning,
        Color.FromRgb(236, 72, 153),
        Purple,
        Color.FromRgb(14, 165, 233),
        Success,
        Color.FromRgb(249, 115, 22),
        Color.FromRgb(99, 102, 241),
        Color.FromRgb(20, 184, 166),
    ];
}
