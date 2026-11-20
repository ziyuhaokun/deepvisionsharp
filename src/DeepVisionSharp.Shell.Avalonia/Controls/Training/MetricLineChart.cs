using System.Globalization;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

using DeepVisionSharp.Presentation.ViewModels;

/**
 * [INPUT]: depends on TrainingChartSeries / TrainingChartPoint from DeepVisionSharp.Presentation.ViewModels; depends on Avalonia Control / StyledProperty / DrawingContext / FormattedText APIs; reads IvText2Brush and IvStrokeBrush from Application.Current resources
 * [OUTPUT]: exports the MetricLineChart control, the MetricChartYScale enum, and the SeriesSource / MinimumY / MaximumY / YScale / EmptyText styled properties
 * [POS]: Avalonia port of Shell/Views/Training/Controls/MetricLineChart; the self-drawn line chart for training metrics. TrainingRunPanel hosts it. It paints legend, grid, axis labels, polyline series and the log-scale mapping itself, with no child visuals.
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
    private const double LeftMargin = 56;
    private const double RightMargin = 14;
    private const double TopMargin = 10;
    private const double BottomMargin = 30;
    private const int GridDivisions = 4;
    private const double LogRangePadding = 0.05;

    private static readonly Typeface ChartTypeface = new("Segoe UI");

    private static readonly IBrush[] SeriesBrushes =
    [
        CreateBrush(59, 130, 246),
        CreateBrush(16, 185, 129),
        CreateBrush(245, 158, 11),
        CreateBrush(239, 68, 68),
        CreateBrush(139, 92, 246),
        CreateBrush(6, 182, 212)
    ];

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

        var textBrush = FindBrush("IvText2Brush", Brushes.DimGray);
        var gridBrush = FindBrush("IvStrokeBrush", Brushes.LightGray);
        var useLog = YScale == MetricChartYScale.Logarithmic;

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
                new Point(Bounds.Width / 2, Bounds.Height / 2),
                TextAlignment.Center);
            return;
        }

        var legendColumns = Math.Min(3, series.Count);
        var legendRows = (int)Math.Ceiling(series.Count / (double)legendColumns);
        var legendHeight = legendRows * 20.0;
        var plot = new Rect(
            LeftMargin,
            TopMargin + legendHeight,
            Math.Max(0, Bounds.Width - LeftMargin - RightMargin),
            Math.Max(0, Bounds.Height - TopMargin - legendHeight - BottomMargin));

        if (plot.Width < 40 || plot.Height < 40)
        {
            return;
        }

        DrawLegend(drawingContext, series, legendColumns, textBrush);

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
                new Point(Bounds.Width / 2, Bounds.Height / 2),
                TextAlignment.Center);
            return;
        }

        DrawGrid(
            drawingContext,
            plot,
            minimumEpoch,
            maximumEpoch,
            minimumY,
            maximumY,
            logMinimum,
            logMaximum,
            useLog,
            gridBrush,
            textBrush);
        DrawSeries(
            drawingContext,
            plot,
            series,
            minimumEpoch,
            maximumEpoch,
            minimumY,
            maximumY,
            logMinimum,
            logMaximum,
            useLog);
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

    private void DrawLegend(
        DrawingContext drawingContext,
        IReadOnlyList<TrainingChartSeries> series,
        int columns,
        IBrush textBrush)
    {
        var itemWidth = Math.Max(1, (Bounds.Width - LeftMargin - RightMargin) / columns);
        for (var index = 0; index < series.Count; index++)
        {
            var row = index / columns;
            var column = index % columns;
            var x = LeftMargin + column * itemWidth;
            var y = TopMargin + row * 20;
            var brush = SeriesBrushes[index % SeriesBrushes.Length];

            drawingContext.DrawLine(new Pen(brush, 2), new Point(x, y + 7), new Point(x + 16, y + 7));
            DrawText(drawingContext, series[index].Name, 11, textBrush, new Point(x + 22, y));
        }
    }

    private void DrawGrid(
        DrawingContext drawingContext,
        Rect plot,
        int minimumEpoch,
        int maximumEpoch,
        double minimumY,
        double maximumY,
        double logMinimum,
        double logMaximum,
        bool useLog,
        IBrush gridBrush,
        IBrush textBrush)
    {
        var gridPen = new Pen(gridBrush, 0.75);
        for (var index = 0; index <= GridDivisions; index++)
        {
            var ratio = index / (double)GridDivisions;
            var y = plot.Bottom - ratio * plot.Height;
            drawingContext.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));

            var value = useLog
                ? Math.Pow(10, logMinimum + ratio * (logMaximum - logMinimum))
                : minimumY + ratio * (maximumY - minimumY);
            DrawText(
                drawingContext,
                FormatYValue(value, maximumY, useLog),
                10,
                textBrush,
                new Point(plot.Left - 8, y - 6),
                TextAlignment.Right);
        }

        var epochLabels = new[]
        {
            minimumEpoch,
            minimumEpoch + (maximumEpoch - minimumEpoch) / 2,
            maximumEpoch
        };

        for (var index = 0; index < epochLabels.Length; index++)
        {
            var ratio = index / 2.0;
            var x = plot.Left + ratio * plot.Width;
            drawingContext.DrawLine(gridPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
            DrawText(
                drawingContext,
                epochLabels[index].ToString(CultureInfo.InvariantCulture),
                10,
                textBrush,
                new Point(x, plot.Bottom + 8),
                TextAlignment.Center);
        }
    }

    private static void DrawSeries(
        DrawingContext drawingContext,
        Rect plot,
        IReadOnlyList<TrainingChartSeries> series,
        int minimumEpoch,
        int maximumEpoch,
        double minimumY,
        double maximumY,
        double logMinimum,
        double logMaximum,
        bool useLog)
    {
        for (var index = 0; index < series.Count; index++)
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

            var brush = SeriesBrushes[index % SeriesBrushes.Length];
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.BeginFigure(points[0], false);
                if (points.Count > 1)
                {
                    foreach (var point in points.Skip(1))
                    {
                        context.LineTo(point);
                    }
                }

                context.EndFigure(false);
            }

            var pen = new Pen(brush, 2, null, PenLineCap.Round, PenLineJoin.Round);
            drawingContext.DrawGeometry(null, pen, geometry);
            drawingContext.DrawEllipse(brush, null, points[^1], 3, 3);
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

    private void DrawText(
        DrawingContext drawingContext,
        string text,
        double fontSize,
        IBrush brush,
        Point origin,
        TextAlignment alignment = TextAlignment.Left)
    {
        var formattedText = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            ChartTypeface,
            fontSize,
            brush);

        var x = alignment switch
        {
            TextAlignment.Center => origin.X - formattedText.Width / 2,
            TextAlignment.Right => origin.X - formattedText.Width,
            _ => origin.X
        };
        drawingContext.DrawText(formattedText, new Point(x, origin.Y));
    }

    private static IBrush FindBrush(string resourceKey, IBrush defaultBrush)
    {
        if (global::Avalonia.Application.Current?.Resources.TryGetResource(resourceKey, null, out var value) == true
            && value is IBrush brush)
        {
            return brush;
        }

        return defaultBrush;
    }

    private static string FormatYValue(double value, double maximumY, bool useLog)
    {
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

    private static IBrush CreateBrush(byte red, byte green, byte blue) =>
        new SolidColorBrush(Color.FromRgb(red, green, blue));
}