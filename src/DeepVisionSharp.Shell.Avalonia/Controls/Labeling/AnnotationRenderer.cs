using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace DeepVisionSharp.Shell.Avalonia.Controls.Labeling;

/// <summary>标注绘制原语：与 WPF 版同一套色值、线宽与标签摆放策略。</summary>
internal static class AnnotationRenderer
{
    private static readonly Color DefaultBoxColor = Color.FromRgb(59, 130, 246);
    private static readonly Typeface LabelTypeface = new("Segoe UI Semibold");
    private static readonly Typeface TextTypeface = new("Segoe UI");

    public static Color ParseColor(string? value, Color? fallback = null)
    {
        var resolvedFallback = fallback ?? DefaultBoxColor;
        if (string.IsNullOrWhiteSpace(value))
        {
            return resolvedFallback;
        }

        return Color.TryParse(value, out var color) ? color : resolvedFallback;
    }

    public static void DrawImageFrame(DrawingContext dc, IImage image, Rect screenRect)
    {
        dc.DrawRectangle(Brushes.White, null, screenRect);
        dc.DrawImage(image, screenRect);
    }

    public static void DrawCanvasAnnotation(
        DrawingContext dc,
        Rect screenRect,
        Color color,
        bool selected,
        bool primary,
        bool showLabel,
        string label,
        double handleSize,
        bool transparentFill,
        AnnotationLabelPlacer? placer = null)
    {
        var fillColor = transparentFill
            ? Color.FromArgb(0, color.R, color.G, color.B)
            : Color.FromArgb(72, color.R, color.G, color.B);
        var fill = new SolidColorBrush(fillColor);
        var pen = new Pen(new SolidColorBrush(color), primary ? 2.4 : selected ? 2.0 : 1.6);
        dc.DrawRectangle(fill, pen, screenRect);

        if (showLabel)
        {
            DrawShapeLabel(dc, label, color, screenRect, placer);
        }

        if (selected)
        {
            DrawResizeHandles(dc, screenRect, color, handleSize);
        }
    }

    public static void DrawOverviewAnnotation(DrawingContext dc, Rect screenRect, Color color)
    {
        var fill = new SolidColorBrush(Color.FromArgb(38, color.R, color.G, color.B));
        var pen = new Pen(new SolidColorBrush(color), 1);
        dc.DrawRectangle(fill, pen, screenRect);
    }

    public static void DrawCanvasQuad(
        DrawingContext dc,
        IReadOnlyList<Point> points,
        Rect bounds,
        Color color,
        bool selected,
        bool primary,
        bool showLabel,
        string text,
        double handleSize,
        bool transparentFill,
        AnnotationLabelPlacer? placer = null)
    {
        var fill = new SolidColorBrush(Color.FromArgb(transparentFill ? (byte)0 : (byte)72, color.R, color.G, color.B));
        var pen = new Pen(new SolidColorBrush(color), primary ? 2.4 : selected ? 2 : 1.6);
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(points[0], true);
            foreach (var point in points.Skip(1))
            {
                context.LineTo(point);
            }

            context.EndFigure(true);
        }

        dc.DrawGeometry(fill, pen, geometry);
        if (showLabel)
        {
            DrawShapeLabel(dc, text, color, bounds, placer);
        }

        if (selected)
        {
            foreach (var point in points)
            {
                dc.DrawRectangle(
                    Brushes.White,
                    pen,
                    new Rect(point.X - handleSize / 2, point.Y - handleSize / 2, handleSize, handleSize));
            }
        }
    }

    public static void DrawPreviewBox(DrawingContext dc, Rect screenRect, Color color)
    {
        var fill = new SolidColorBrush(Color.FromArgb(28, color.R, color.G, color.B));
        var pen = new Pen(new SolidColorBrush(color), 1.5) { DashStyle = new DashStyle([4, 2], 0) };
        dc.DrawRectangle(fill, pen, screenRect);
    }

    public static void DrawMarquee(DrawingContext dc, Rect screenRect)
    {
        var color = DefaultBoxColor;
        var fill = new SolidColorBrush(Color.FromArgb(36, color.R, color.G, color.B));
        var pen = new Pen(new SolidColorBrush(color), 1.4) { DashStyle = new DashStyle([4, 2], 0) };
        dc.DrawRectangle(fill, pen, screenRect);
    }

    public static void DrawViewportIndicator(DrawingContext dc, Rect screenRect)
    {
        var color = DefaultBoxColor;
        var fill = new SolidColorBrush(Color.FromArgb(18, color.R, color.G, color.B));
        var pen = new Pen(new SolidColorBrush(color), 1.6);
        dc.DrawRectangle(fill, pen, screenRect);
    }

    public static void DrawCenteredText(
        DrawingContext dc,
        string text,
        Size renderSize,
        IBrush foreground,
        double fontSize,
        bool semibold = false)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            semibold ? LabelTypeface : TextTypeface,
            fontSize,
            foreground);

        dc.DrawText(
            formatted,
            new Point((renderSize.Width - formatted.Width) / 2, (renderSize.Height - formatted.Height) / 2));
    }

    public static void DrawShapeLabel(
        DrawingContext dc,
        string label,
        Color color,
        Rect rect,
        AnnotationLabelPlacer? placer = null)
    {
        var text = string.IsNullOrWhiteSpace(label) ? "object" : label;
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            LabelTypeface,
            12,
            Brushes.White);

        var labelSize = new Size(formatted.Width + 12, formatted.Height + 6);
        var labelRect = placer is null
            ? new Rect(rect.Left, Math.Max(0, rect.Top - formatted.Height - 6), labelSize.Width, labelSize.Height)
            : placer.Place(rect, labelSize);
        DrawLabelChip(dc, formatted, color, labelRect, 6, 3, 4);
    }

    private static void DrawLabelChip(
        DrawingContext dc,
        FormattedText formatted,
        Color color,
        Rect labelRect,
        double textLeft,
        double textTop,
        double cornerRadius)
    {
        var brush = new SolidColorBrush(color);
        dc.DrawRectangle(brush, null, labelRect, cornerRadius, cornerRadius);
        dc.DrawText(formatted, new Point(labelRect.Left + textLeft, labelRect.Top + textTop));
    }

    private static void DrawResizeHandles(DrawingContext dc, Rect rect, Color color, double handleSize)
    {
        var pen = new Pen(new SolidColorBrush(color), 1.4);
        foreach (var center in new[]
                 {
                     rect.TopLeft,
                     rect.TopRight,
                     rect.BottomLeft,
                     rect.BottomRight,
                 })
        {
            dc.DrawRectangle(
                Brushes.White,
                pen,
                new Rect(center.X - handleSize / 2, center.Y - handleSize / 2, handleSize, handleSize));
        }
    }
}

internal sealed class AnnotationLabelPlacer
{
    private readonly List<Rect> _placed = new();

    public Rect Place(Rect boxRect, Size labelSize, double padding = 4)
        => TryPlace(boxRect, labelSize, padding) ?? Fallback(boxRect, labelSize, padding);

    public Rect? TryPlace(Rect boxRect, Size labelSize, double padding = 4)
    {
        foreach (var candidate in GetCandidates(boxRect, labelSize, padding))
        {
            if (!IntersectsPlaced(candidate))
            {
                _placed.Add(candidate);
                return candidate;
            }
        }

        return null;
    }

    private Rect Fallback(Rect boxRect, Size labelSize, double padding)
    {
        var fallback = GetCandidates(boxRect, labelSize, padding)[0];
        _placed.Add(fallback);
        return fallback;
    }

    private static Rect[] GetCandidates(Rect boxRect, Size labelSize, double padding) =>
    [
        new Rect(boxRect.Left, Math.Max(0, boxRect.Top - labelSize.Height - padding), labelSize.Width, labelSize.Height),
        new Rect(boxRect.Left, boxRect.Bottom + padding, labelSize.Width, labelSize.Height),
        new Rect(boxRect.Left, boxRect.Top, labelSize.Width, labelSize.Height),
        new Rect(Math.Max(0, boxRect.Left - labelSize.Width - padding), boxRect.Top, labelSize.Width, labelSize.Height),
        new Rect(boxRect.Right + padding, boxRect.Top, labelSize.Width, labelSize.Height),
    ];

    private bool IntersectsPlaced(Rect candidate)
        => _placed.Any(placed => candidate.Intersects(placed));
}