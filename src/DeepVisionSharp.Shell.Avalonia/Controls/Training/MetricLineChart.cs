using System.Globalization;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

using DeepVisionSharp.Presentation.ViewModels;

/**
 * [INPUT]: depends on TrainingChartSeries / TrainingChartPoint from DeepVisionSharp.Presentation.ViewModels; depends on Avalonia Control / StyledProperty / DrawingContext / FormattedText APIs; reads IvText1Brush / IvText2Brush / IvText3Brush / IvStrokeBrush from Application.Current resources
 * [OUTPUT]: exports the MetricLineChart control, the MetricChartYScale enum, and the SeriesSource / MinimumY / MaximumY / YScale / EmptyText styled properties
 * [POS]: the self-drawn chart for training metrics. TrainingRunPanel hosts it. Style: no grid, smooth monotone-clamped curves with a soft gradient fill, a dashed baseline, min/max Y labels, first/last epoch labels, and an inline legend that carries each series' latest value. First series uses the primary text colour, the second the tertiary text colour, the rest use an accent palette.
 * [PROTOCOL]: Update this header on change, then check CLAUDE.md
 */
namespace DeepVisionSharp.Shell.Avalonia.Controls.Training;

public enum MetricChartYScale
{
    Linear = 0,
    Logarithmic = 1
}

public sealed class MetricLineChart : Control
{
    private const double LeftMargin = 62;
    private const double RightMargin = 14;
    private const double TopMargin = 6;
    private const double BottomMargin = 28;
    private const double LegendRowHeight = 22;
    private const double LegendGap = 20;
    private const double LogRangePadding = 0.05;

    private static readonly Typeface ChartTypeface = new("Segoe UI");
    private static readonly Typeface MonoTypeface = new("Cascadia Mono, Consolas");

    // Series 0 and 1 follow the theme text colours (see ResolveSeriesColor); these are series 2+.
    private static readonly Color[] AccentColors =
    [
        Color.FromRgb(59, 130, 246),
        Color.FromRgb(16, 185, 129),
        Color.FromRgb(245, 158, 11),
        Color.FromRgb(239, 68, 68)
    ];

    // Filled by Render, read by the pointer handlers: where the plot is and which epochs exist.
    private Rect _plotRect;
    private int[] _epochs = [];
    private int _axisMinEpoch;
    private int _axisMaxEpoch;
    private int? _hoverEpoch;

    public static readonly StyledProperty<IReadOnlyList<TrainingChartSeries>?> SeriesSourceProperty =
        AvaloniaProperty.Register<MetricLineChart, IReadOnlyList<TrainingChartSeries>?>(nameof(SeriesSource));

    public static readonly StyledProperty<double> MinimumYProperty =
        AvaloniaProperty.Register<MetricLineChart, double>(nameof(MinimumY), defaultValue: double.NaN);

    public static readonly StyledProperty<double> MaximumYProperty =
        AvaloniaProperty.Register<MetricLineChart, double>(nameof(MaximumY), defaultValue: double.NaN);

    public static readonly StyledProperty<MetricChartYScale> YScaleProperty =
        AvaloniaProperty.Register<MetricLineChart, MetricChartYScale>(nameof(YScale));

    public static readonly StyledProperty<string> EmptyTextProperty =
        AvaloniaProperty.Register<MetricLineChart, string>(nameof(EmptyText), defaultValue: "暂无曲线数据");

    static MetricLineChart()
    {
        AffectsRender<MetricLineChart>(
            SeriesSourceProperty,
            MinimumYProperty,
            MaximumYProperty,
            YScaleProperty,
            EmptyTextProperty);
    }

    public MetricLineChart()
    {
        UseLayoutRounding = true;
        ClipToBounds = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_epochs.Length == 0 || _plotRect.Width <= 0)
        {
            return;
        }

        var position = e.GetPosition(this);
        int? next = null;
        if (position.X >= _plotRect.Left - 12 && position.X <= _plotRect.Right + 12
            && position.Y >= _plotRect.Top - 12 && position.Y <= _plotRect.Bottom + 28)
        {
            var span = Math.Max(1, _axisMaxEpoch - _axisMinEpoch);
            var target = _axisMinEpoch + (position.X - _plotRect.Left) / _plotRect.Width * span;
            next = _epochs.MinBy(epoch => Math.Abs(epoch - target));
        }

        if (next != _hoverEpoch)
        {
            _hoverEpoch = next;
            InvalidateVisual();
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_hoverEpoch is not null)
        {
            _hoverEpoch = null;
            InvalidateVisual();
        }
    }

    public IReadOnlyList<TrainingChartSeries>? SeriesSource
    {
        get => GetValue(SeriesSourceProperty);
        set => SetValue(SeriesSourceProperty, value);
    }

    public double MinimumY
    {
        get => GetValue(MinimumYProperty);
        set => SetValue(MinimumYProperty, value);
    }

    public double MaximumY
    {
        get => GetValue(MaximumYProperty);
        set => SetValue(MaximumYProperty, value);
    }

    public MetricChartYScale YScale
    {
        get => GetValue(YScaleProperty);
        set => SetValue(YScaleProperty, value);
    }

    public string EmptyText
    {
        get => GetValue(EmptyTextProperty) ?? "暂无曲线数据";
        set => SetValue(EmptyTextProperty, value);
    }

    public override void Render(DrawingContext drawingContext)
    {
        base.Render(drawingContext);

        // A control only receives pointer events where it paints something.
        drawingContext.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size));
        _epochs = [];
        _plotRect = default;

        var textBrush = FindBrush("IvText2Brush", Brushes.DimGray);
        var mutedBrush = FindBrush("IvText3Brush", Brushes.Gray);
        var strongBrush = FindBrush("IvText1Brush", Brushes.Black);
        var baselineBrush = FindBrush("IvStrokeBrush", Brushes.LightGray);
        var useLog = YScale == MetricChartYScale.Logarithmic;
        var isRatio = !useLog && double.IsFinite(MaximumY) && MaximumY <= 1.0;

        var series = SeriesSource?
            .Where(item => item.Points.Any(point => IsPlottable(point.Value, useLog)))
            .ToList() ?? [];

        if (series.Count == 0)
        {
            var emptyMessage = useLog && SeriesSource is { Count: > 0 }
                ? "暂无可用正值（对数坐标）"
                : EmptyText;
            DrawText(
                drawingContext,
                emptyMessage,
                12,
                textBrush,
                new Point(Bounds.Width / 2, Bounds.Height / 2 - 8),
                TextAlignment.Center);
            return;
        }

        var colors = series.Select((_, index) => ResolveSeriesColor(index)).ToList();
        var legendRows = DrawLegend(drawingContext, series, colors, useLog, isRatio, textBrush, strongBrush);
        var legendHeight = legendRows * LegendRowHeight;

        var plot = new Rect(
            LeftMargin,
            TopMargin + legendHeight + 6,
            Math.Max(0, Bounds.Width - LeftMargin - RightMargin),
            Math.Max(0, Bounds.Height - TopMargin - legendHeight - 6 - BottomMargin));

        if (plot.Width < 40 || plot.Height < 24)
        {
            return;
        }

        var points = series
            .SelectMany(item => item.Points)
            .Where(point => IsPlottable(point.Value, useLog))
            .ToList();
        var minimumEpoch = points.Min(point => point.Epoch);
        var maximumEpoch = points.Max(point => point.Epoch);
        if (minimumEpoch == maximumEpoch)
        {
            maximumEpoch = minimumEpoch + 1;
        }

        if (!TryResolveYRange(points, useLog, out var minimumY, out var maximumY, out var logMinimum, out var logMaximum))
        {
            DrawText(
                drawingContext,
                "暂无可用正值（对数坐标）",
                12,
                textBrush,
                new Point(Bounds.Width / 2, Bounds.Height / 2 - 8),
                TextAlignment.Center);
            return;
        }

        _plotRect = plot;
        _axisMinEpoch = minimumEpoch;
        _axisMaxEpoch = maximumEpoch;
        _epochs = points.Select(point => point.Epoch).Distinct().Order().ToArray();
        if (_hoverEpoch is { } hovered && !_epochs.Contains(hovered))
        {
            _hoverEpoch = null;
        }

        DrawAxes(
            drawingContext,
            plot,
            minimumEpoch,
            maximumEpoch,
            minimumY,
            maximumY,
            useLog,
            isRatio,
            baselineBrush,
            mutedBrush);
        DrawSeries(
            drawingContext,
            plot,
            series,
            colors,
            minimumEpoch,
            maximumEpoch,
            minimumY,
            maximumY,
            logMinimum,
            logMaximum,
            useLog);

        if (_hoverEpoch is { } epoch)
        {
            DrawHover(
                drawingContext,
                plot,
                series,
                colors,
                epoch,
                minimumEpoch,
                maximumEpoch,
                minimumY,
                maximumY,
                logMinimum,
                logMaximum,
                useLog,
                isRatio,
                textBrush,
                strongBrush,
                mutedBrush);
        }
    }

    private bool TryResolveYRange(
        IReadOnlyList<TrainingChartPoint> points,
        bool useLog,
        out double minimumY,
        out double maximumY,
        out double logMinimum,
        out double logMaximum)
    {
        minimumY = 0;
        maximumY = 1;
        logMinimum = 0;
        logMaximum = 1;

        if (useLog)
        {
            var positiveValues = points.Select(point => point.Value).Where(value => value > 0).ToArray();
            if (positiveValues.Length == 0)
            {
                return false;
            }

            var dataMin = positiveValues.Min();
            var dataMax = positiveValues.Max();
            minimumY = double.IsFinite(MinimumY) && MinimumY > 0 ? MinimumY : dataMin;
            maximumY = double.IsFinite(MaximumY) && MaximumY > 0 ? MaximumY : dataMax;
            if (maximumY < minimumY)
            {
                (minimumY, maximumY) = (maximumY, minimumY);
            }

            logMinimum = Math.Log10(minimumY);
            logMaximum = Math.Log10(maximumY);
            if (Math.Abs(logMaximum - logMinimum) < 1e-9)
            {
                logMinimum -= 0.5;
                logMaximum += 0.5;
            }
            else
            {
                var pad = (logMaximum - logMinimum) * LogRangePadding;
                logMinimum -= pad;
                logMaximum += pad;
            }

            minimumY = Math.Pow(10, logMinimum);
            maximumY = Math.Pow(10, logMaximum);
            return true;
        }

        minimumY = double.IsFinite(MinimumY) ? MinimumY : 0;
        maximumY = double.IsFinite(MaximumY) ? MaximumY : points.Max(point => point.Value) * 1.08;
        if (maximumY <= minimumY)
        {
            maximumY = minimumY + 1;
        }

        logMinimum = minimumY;
        logMaximum = maximumY;
        return true;
    }

    /// <summary>One inline row of "dot  name  latest value"; wraps when the width runs out. Returns the row count.</summary>
    private int DrawLegend(
        DrawingContext drawingContext,
        IReadOnlyList<TrainingChartSeries> series,
        IReadOnlyList<Color> colors,
        bool useLog,
        bool isRatio,
        IBrush nameBrush,
        IBrush valueBrush)
    {
        var maxX = Math.Max(120, Bounds.Width - RightMargin);
        var x = 0.0;
        var row = 0;

        for (var index = 0; index < series.Count; index++)
        {
            var name = series[index].Name;
            var last = series[index].Points
                .Where(point => IsPlottable(point.Value, useLog))
                .OrderBy(point => point.Epoch)
                .LastOrDefault();
            var valueText = last is null ? string.Empty : FormatLatest(last.Value, isRatio);

            var nameText = CreateText(name, 11.5, nameBrush, ChartTypeface);
            var valueFormatted = CreateText(valueText, 11.5, valueBrush, MonoTypeface);
            var itemWidth = 14 + nameText.Width + (valueText.Length > 0 ? 8 + valueFormatted.Width : 0);

            if (x > 0 && x + itemWidth > maxX)
            {
                row++;
                x = 0;
            }

            var y = TopMargin + row * LegendRowHeight;
            drawingContext.DrawEllipse(new SolidColorBrush(colors[index]), null, new Point(x + 4, y + 8), 3.5, 3.5);
            drawingContext.DrawText(nameText, new Point(x + 14, y));
            if (valueText.Length > 0)
            {
                drawingContext.DrawText(valueFormatted, new Point(x + 14 + nameText.Width + 8, y));
            }

            x += itemWidth + LegendGap;
        }

        return row + 1;
    }

    /// <summary>No grid: a dashed baseline, min/max Y labels, first/last epoch labels.</summary>
    private void DrawAxes(
        DrawingContext drawingContext,
        Rect plot,
        int minimumEpoch,
        int maximumEpoch,
        double minimumY,
        double maximumY,
        bool useLog,
        bool isRatio,
        IBrush baselineBrush,
        IBrush labelBrush)
    {
        var dashed = new Pen(baselineBrush, 1, new DashStyle([3, 4], 0));
        drawingContext.DrawLine(dashed, new Point(plot.Left, plot.Bottom), new Point(plot.Right, plot.Bottom));

        DrawText(
            drawingContext,
            FormatAxisValue(maximumY, maximumY, useLog, isRatio),
            10.5,
            labelBrush,
            new Point(plot.Left - 10, plot.Top - 7),
            TextAlignment.Right,
            MonoTypeface);
        DrawText(
            drawingContext,
            FormatAxisValue(minimumY, maximumY, useLog, isRatio),
            10.5,
            labelBrush,
            new Point(plot.Left - 10, plot.Bottom - 7),
            TextAlignment.Right,
            MonoTypeface);

        DrawText(
            drawingContext,
            $"第 {minimumEpoch} 轮",
            11,
            labelBrush,
            new Point(plot.Left, plot.Bottom + 9));
        DrawText(
            drawingContext,
            $"第 {maximumEpoch} 轮",
            11,
            labelBrush,
            new Point(plot.Right, plot.Bottom + 9),
            TextAlignment.Right);
    }

    private static void DrawSeries(
        DrawingContext drawingContext,
        Rect plot,
        IReadOnlyList<TrainingChartSeries> series,
        IReadOnlyList<Color> colors,
        int minimumEpoch,
        int maximumEpoch,
        double minimumY,
        double maximumY,
        double logMinimum,
        double logMaximum,
        bool useLog)
    {
        // Draw back to front so series 0 (the primary one) ends up on top.
        for (var index = series.Count - 1; index >= 0; index--)
        {
            var points = series[index].Points
                .Where(point => IsPlottable(point.Value, useLog))
                .OrderBy(point => point.Epoch)
                .Select(point => new Point(
                    plot.Left + (point.Epoch - minimumEpoch) / (double)(maximumEpoch - minimumEpoch) * plot.Width,
                    MapValueToY(plot, point.Value, minimumY, maximumY, logMinimum, logMaximum, useLog)))
                .ToList();

            if (points.Count == 0)
            {
                continue;
            }

            var color = colors[index];
            var brush = new SolidColorBrush(color);

            if (points.Count > 1)
            {
                var fill = new StreamGeometry();
                using (var context = fill.Open())
                {
                    context.BeginFigure(points[0], true);
                    AppendSmoothSegments(context, points);
                    context.LineTo(new Point(points[^1].X, plot.Bottom));
                    context.LineTo(new Point(points[0].X, plot.Bottom));
                    context.EndFigure(true);
                }

                var topAlpha = index == 0 ? (byte)78 : (byte)20;
                var gradient = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                    GradientStops =
                    [
                        new GradientStop(Color.FromArgb(topAlpha, color.R, color.G, color.B), 0),
                        new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1)
                    ]
                };
                drawingContext.DrawGeometry(gradient, null, fill);

                var line = new StreamGeometry();
                using (var context = line.Open())
                {
                    context.BeginFigure(points[0], false);
                    AppendSmoothSegments(context, points);
                    context.EndFigure(false);
                }

                var pen = new Pen(brush, index == 0 ? 2.4 : 2, null, PenLineCap.Round, PenLineJoin.Round);
                drawingContext.DrawGeometry(null, pen, line);
            }

            var end = points[^1];
            drawingContext.DrawEllipse(new SolidColorBrush(Color.FromArgb(40, color.R, color.G, color.B)), null, end, 7, 7);
            drawingContext.DrawEllipse(brush, null, end, 3.6, 3.6);
        }
    }

    /// <summary>Hover layer: vertical guide, a ring on each series at that epoch, and a floating value card.</summary>
    private void DrawHover(
        DrawingContext drawingContext,
        Rect plot,
        IReadOnlyList<TrainingChartSeries> series,
        IReadOnlyList<Color> colors,
        int epoch,
        int minimumEpoch,
        int maximumEpoch,
        double minimumY,
        double maximumY,
        double logMinimum,
        double logMaximum,
        bool useLog,
        bool isRatio,
        IBrush nameBrush,
        IBrush valueBrush,
        IBrush guideBrush)
    {
        var x = plot.Left + (epoch - minimumEpoch) / (double)(maximumEpoch - minimumEpoch) * plot.Width;
        var guide = new Pen(guideBrush, 1, new DashStyle([3, 3], 0));
        drawingContext.DrawLine(guide, new Point(x, plot.Top), new Point(x, plot.Bottom));

        var rows = new List<(Color Color, FormattedText Name, FormattedText Value)>();
        for (var index = 0; index < series.Count; index++)
        {
            var point = series[index].Points.FirstOrDefault(item => item.Epoch == epoch && IsPlottable(item.Value, useLog));
            if (point is null)
            {
                continue;
            }

            var y = MapValueToY(plot, point.Value, minimumY, maximumY, logMinimum, logMaximum, useLog);
            var color = colors[index];
            drawingContext.DrawEllipse(new SolidColorBrush(Color.FromArgb(48, color.R, color.G, color.B)), null, new Point(x, y), 8, 8);
            drawingContext.DrawEllipse(FindBrush("IvSurface2Brush", Brushes.White), new Pen(new SolidColorBrush(color), 2), new Point(x, y), 4, 4);

            rows.Add((
                color,
                CreateText(series[index].Name, 11.5, nameBrush, ChartTypeface),
                CreateText(FormatLatest(point.Value, isRatio), 11.5, valueBrush, MonoTypeface)));
        }

        if (rows.Count == 0)
        {
            return;
        }

        var header = CreateText($"第 {epoch} 轮", 11.5, valueBrush, ChartTypeface);
        var nameWidth = rows.Max(row => row.Name.Width);
        var valueWidth = rows.Max(row => row.Value.Width);
        var width = Math.Max(header.Width, 14 + nameWidth + 16 + valueWidth) + 24;
        var height = 12 + header.Height + 6 + rows.Count * 20 + 4;

        var left = x + 14 + width <= Bounds.Width - 4 ? x + 14 : x - 14 - width;
        left = Math.Max(2, left);
        var top = Math.Clamp(plot.Top + 4, 2, Math.Max(2, Bounds.Height - height - 2));
        var box = new Rect(left, top, width, height);

        var background = FindBrush("IvSurface2Brush", Brushes.White);
        var border = new Pen(FindBrush("IvStrokeBrush", Brushes.LightGray), 1);
        // soft drop shadow: two translucent offsets, no hard edge
        drawingContext.DrawRectangle(new SolidColorBrush(Color.FromArgb(22, 0, 0, 0)), null, box.Translate(new Vector(0, 3)).Inflate(1), 10, 10);
        drawingContext.DrawRectangle(background, border, box, 10, 10);

        drawingContext.DrawText(header, new Point(box.Left + 12, box.Top + 8));
        var rowY = box.Top + 8 + header.Height + 6;
        foreach (var row in rows)
        {
            drawingContext.DrawEllipse(new SolidColorBrush(row.Color), null, new Point(box.Left + 16, rowY + 8), 3.5, 3.5);
            drawingContext.DrawText(row.Name, new Point(box.Left + 26, rowY));
            drawingContext.DrawText(row.Value, new Point(box.Right - 12 - row.Value.Width, rowY));
            rowY += 20;
        }
    }

    /// <summary>
    /// Catmull-Rom → cubic Bezier. Control-point Y is clamped between the two neighbours so the curve
    /// never overshoots the data (a smoothed accuracy curve must not dip above 100% or below 0).
    /// </summary>
    private static void AppendSmoothSegments(StreamGeometryContext context, IReadOnlyList<Point> points)
    {
        for (var i = 0; i < points.Count - 1; i++)
        {
            var p0 = points[Math.Max(i - 1, 0)];
            var p1 = points[i];
            var p2 = points[i + 1];
            var p3 = points[Math.Min(i + 2, points.Count - 1)];

            var low = Math.Min(p1.Y, p2.Y);
            var high = Math.Max(p1.Y, p2.Y);
            var c1 = new Point(p1.X + (p2.X - p0.X) / 6, Math.Clamp(p1.Y + (p2.Y - p0.Y) / 6, low, high));
            var c2 = new Point(p2.X - (p3.X - p1.X) / 6, Math.Clamp(p2.Y - (p3.Y - p1.Y) / 6, low, high));
            context.CubicBezierTo(c1, c2, p2);
        }
    }

    private static double MapValueToY(
        Rect plot,
        double value,
        double minimumY,
        double maximumY,
        double logMinimum,
        double logMaximum,
        bool useLog)
    {
        if (useLog)
        {
            var logValue = Math.Log10(value);
            return plot.Bottom - (logValue - logMinimum) / (logMaximum - logMinimum) * plot.Height;
        }

        return plot.Bottom - (value - minimumY) / (maximumY - minimumY) * plot.Height;
    }

    private static bool IsPlottable(double value, bool useLog) =>
        double.IsFinite(value) && (!useLog || value > 0);

    private static FormattedText CreateText(string text, double fontSize, IBrush brush, Typeface typeface) =>
        new(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            brush);

    private void DrawText(
        DrawingContext drawingContext,
        string text,
        double fontSize,
        IBrush brush,
        Point origin,
        TextAlignment alignment = TextAlignment.Left,
        Typeface? typeface = null)
    {
        var formattedText = CreateText(text, fontSize, brush, typeface ?? ChartTypeface);

        var x = alignment switch
        {
            TextAlignment.Center => origin.X - formattedText.Width / 2,
            TextAlignment.Right => origin.X - formattedText.Width,
            _ => origin.X
        };
        drawingContext.DrawText(formattedText, new Point(x, origin.Y));
    }

    private static Color ResolveSeriesColor(int index)
    {
        switch (index)
        {
            case 0:
                return FindColor("IvText1Brush", Color.FromRgb(28, 30, 33));
            case 1:
                return FindColor("IvText3Brush", Color.FromRgb(120, 124, 132));
            default:
                return AccentColors[(index - 2) % AccentColors.Length];
        }
    }

    private static Color FindColor(string resourceKey, Color fallback) =>
        FindBrush(resourceKey, new SolidColorBrush(fallback)) is ISolidColorBrush solid
            ? Color.FromRgb(solid.Color.R, solid.Color.G, solid.Color.B)
            : fallback;

    private static IBrush FindBrush(string resourceKey, IBrush defaultBrush)
    {
        if (global::Avalonia.Application.Current is { } app
            && app.TryGetResource(resourceKey, app.ActualThemeVariant, out var value)
            && value is IBrush brush)
        {
            return brush;
        }

        return defaultBrush;
    }

    private static string FormatLatest(double value, bool isRatio)
    {
        if (isRatio)
        {
            return (value * 100).ToString("0.#", CultureInfo.InvariantCulture) + "%";
        }

        var abs = Math.Abs(value);
        if (abs > 0 && abs < 0.001)
        {
            return value.ToString("0.##e0", CultureInfo.InvariantCulture);
        }

        return abs >= 100
            ? value.ToString("0.#", CultureInfo.InvariantCulture)
            : abs >= 1
                ? value.ToString("0.###", CultureInfo.InvariantCulture)
                : value.ToString("0.0000", CultureInfo.InvariantCulture);
    }

    private static string FormatAxisValue(double value, double maximumY, bool useLog, bool isRatio)
    {
        if (isRatio)
        {
            return (value * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
        }

        if (useLog)
        {
            var abs = Math.Abs(value);
            if (abs >= 100 || (abs > 0 && abs < 0.01))
            {
                return value.ToString("0.##e0", CultureInfo.InvariantCulture);
            }

            return value.ToString(abs >= 10 ? "0.#" : "0.###", CultureInfo.InvariantCulture);
        }

        return maximumY <= 1.01
            ? value.ToString("F2", CultureInfo.InvariantCulture)
            : value.ToString(maximumY < 10 ? "F2" : "F1", CultureInfo.InvariantCulture);
    }
}
