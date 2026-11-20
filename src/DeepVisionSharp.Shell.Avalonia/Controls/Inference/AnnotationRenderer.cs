using System.Globalization;

using Avalonia;
using Avalonia.Media;

namespace DeepVisionSharp.Shell.Avalonia.Controls.Inference;

/// <summary>
/// 只读叠加层的绘制原语：色值解析、图像框、检测框与标签胶囊。线宽、标签字号与摆放策略取自
/// deepvision 的 AnnotationRenderer；选中框/缩放手柄那套编辑态装饰不在本版范围内。
/// </summary>
internal static class AnnotationRenderer
{
    private const double LabelFontSize = 12;
    private const double BoxStrokeThickness = 1.6;

    private static readonly Color DefaultBoxColor = Color.FromRgb(59, 130, 246);
    private static readonly Typeface LabelTypeface = new("Segoe UI Semibold");

    public static Color ParseColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return DefaultBoxColor;
        }

        return Color.TryParse(value, out var color) ? color : DefaultBoxColor;
    }

    public static void DrawImageFrame(DrawingContext context, IImage image, Rect screenRect)
    {
        context.DrawRectangle(Brushes.White, null, screenRect);
        context.DrawImage(image, screenRect);
    }

    /// <summary>检测框：透明或 28% 填充（推理页固定 UseTransparentFill）+ 1.6px 实线描边 + 标签胶囊。</summary>
    public static void DrawBox(
        DrawingContext context,
        Rect screenRect,
        Color color,
        bool transparentFill,
        bool showLabel,
        string label,
        AnnotationLabelPlacer placer)
    {
        var fill = new SolidColorBrush(
            Color.FromArgb(transparentFill ? (byte)0 : (byte)72, color.R, color.G, color.B));
        context.DrawRectangle(fill, new Pen(new SolidColorBrush(color), BoxStrokeThickness), screenRect);

        if (showLabel)
        {
            DrawShapeLabel(context, label, color, screenRect, placer);
        }
    }

    public static void DrawCenteredText(
        DrawingContext context,
        string text,
        Size renderSize,
        IBrush foreground,
        double fontSize)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            LabelTypeface,
            fontSize,
            foreground);

        context.DrawText(
            formatted,
            new Point((renderSize.Width - formatted.Width) / 2, (renderSize.Height - formatted.Height) / 2));
    }

    public static void DrawShapeLabel(
        DrawingContext context,
        string label,
        Color color,
        Rect rect,
        AnnotationLabelPlacer placer)
    {
        var text = string.IsNullOrWhiteSpace(label) ? "object" : label;
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            LabelTypeface,
            LabelFontSize,
            Brushes.White);

        var labelSize = new Size(formatted.Width + 12, formatted.Height + 6);
        var labelRect = placer.Place(rect, labelSize);

        context.DrawRectangle(new SolidColorBrush(color), null, labelRect, 4, 4);
        context.DrawText(formatted, new Point(labelRect.Left + 6, labelRect.Top + 3));
    }
}

/// <summary>
/// 标签胶囊避让：优先放在框上方，其次下方、框内、左侧、右侧，取第一个不与已放标签相交的位置。
/// 与 deepvision 的 AnnotationLabelPlacer 同一套候选序列与 4px 间距。
/// </summary>
internal sealed class AnnotationLabelPlacer
{
    private readonly List<Rect> _placed = new();

    public Rect Place(Rect boxRect, Size labelSize, double padding = 4)
    {
        var candidates = GetCandidates(boxRect, labelSize, padding);
        foreach (var candidate in candidates)
        {
            if (!_placed.Any(placed => candidate.Intersects(placed)))
            {
                _placed.Add(candidate);
                return candidate;
            }
        }

        _placed.Add(candidates[0]);
        return candidates[0];
    }

    private static Rect[] GetCandidates(Rect boxRect, Size labelSize, double padding) =>
    [
        new Rect(boxRect.Left, Math.Max(0, boxRect.Top - labelSize.Height - padding), labelSize.Width, labelSize.Height),
        new Rect(boxRect.Left, boxRect.Bottom + padding, labelSize.Width, labelSize.Height),
        new Rect(boxRect.Left, boxRect.Top, labelSize.Width, labelSize.Height),
        new Rect(Math.Max(0, boxRect.Left - labelSize.Width - padding), boxRect.Top, labelSize.Width, labelSize.Height),
        new Rect(boxRect.Right + padding, boxRect.Top, labelSize.Width, labelSize.Height),
    ];
}
