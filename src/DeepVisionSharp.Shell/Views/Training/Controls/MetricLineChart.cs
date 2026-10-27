using System.Globalization;
using System.Windows;
using System.Windows.Media;

using DeepVisionSharp.Shell.ViewModels;

using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace DeepVisionSharp.Shell.Views.Training.Controls;

public enum MetricChartYScale
{
    Linear = 0,
    Logarithmic = 1
}

public sealed class MetricLineChart : FrameworkElement
{
    private const double LeftMargin = 56;
    private const double RightMargin = 14;
    private const double TopMargin = 10;
    private const double BottomMargin = 30;
    private const int GridDivisions = 4;
    private const double LogRangePadding = 0.05;

    private static readonly Brush[] SeriesBrushes =
    [
        CreateBrush(59, 130, 246),
        CreateBrush(16, 185, 129),
        CreateBrush(245, 158, 11),
        CreateBrush(239, 68, 68),
        CreateBrush(139, 92, 246),
        CreateBrush(6, 182, 212)
    ];

    public static readonly DependencyProperty SeriesSourceProperty = DependencyProperty.Register(
        nameof(SeriesSource),
        typeof(IReadOnlyList<TrainingChartSeries>),
        typeof(MetricLineChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MinimumYProperty = DependencyProperty.Register(
        nameof(MinimumY),
        typeof(double),
        typeof(MetricLineChart),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaximumYProperty = DependencyProperty.Register(
        nameof(MaximumY),
        typeof(double),
        typeof(MetricLineChart),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty YScaleProperty = DependencyProperty.Register(
        nameof(YScale),
        typeof(MetricChartYScale),
        typeof(MetricLineChart),
        new FrameworkPropertyMetadata(MetricChartYScale.Linear, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty EmptyTextProperty = DependencyProperty.Register(
        nameof(EmptyText),
        typeof(string),
        typeof(MetricLineChart),
        new FrameworkPropertyMetadata("暂无曲线数据", FrameworkPropertyMetadataOptions.AffectsRender));

    public MetricLineChart()
    {
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
    }

    public IReadOnlyList<TrainingChartSeries>? SeriesSource
    {
        get => (IReadOnlyList<TrainingChartSeries>?)GetValue(SeriesSourceProperty);
        set => SetValue(SeriesSourceProperty, value);
    }

    public double MinimumY
    {
        get => (double)GetValue(MinimumYProperty);
        set => SetValue(MinimumYProperty, value);
    }

    public double MaximumY
    {
        get => (double)GetValue(MaximumYProperty);
        set => SetValue(MaximumYProperty, value);
    }

    public MetricChartYScale YScale
    {
        get => (MetricChartYScale)GetValue(YScaleProperty);
        set => SetValue(YScaleProperty, value);
    }

    public string EmptyText
    {
        get => (string)GetValue(EmptyTextProperty);
        set => SetValue(EmptyTextProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var textBrush = FindBrush("TextFillColorSecondaryBrush", Brushes.DimGray);
        var gridBrush = FindBrush("ControlStrokeColorDefaultBrush", Brushes.LightGray);
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
                new Point(ActualWidth / 2, ActualHeight / 2),
                TextAlignment.Center);
            return;
        }

        var legendColumns = Math.Min(3, series.Count);
        var legendRows = (int)Math.Ceiling(series.Count / (double)legendColumns);
        var legendHeight = legendRows * 20.0;
        var plot = new Rect(
            LeftMargin,
            TopMargin + legendHeight,
            Math.Max(0, ActualWidth - LeftMargin - RightMargin),
            Math.Max(0, ActualHeight - TopMargin - legendHeight - BottomMargin));

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
                new Point(ActualWidth / 2, ActualHeight / 2),
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
        Brush textBrush)
    {
        var itemWidth = Math.Max(1, (ActualWidth - LeftMargin - RightMargin) / columns);
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
        Brush gridBrush,
        Brush textBrush)
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
                context.BeginFigure(points[0], isFilled: false, isClosed: false);
                if (points.Count > 1)
                {
                    context.PolyLineTo(points.Skip(1).ToList(), isStroked: true, isSmoothJoin: true);
                }
            }

            geometry.Freeze();
            var pen = new Pen(brush, 2)
            {
                LineJoin = PenLineJoin.Round,
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
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
        Brush brush,
        Point origin,
        TextAlignment alignment = TextAlignment.Left)
    {
        var formattedText = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            System.Windows.FlowDirection.LeftToRight,
            new Typeface(
                System.Windows.SystemFonts.MessageFontFamily,
                FontStyles.Normal,
                FontWeights.Normal,
                FontStretches.Normal),
            fontSize,
            brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

        var x = alignment switch
        {
            TextAlignment.Center => origin.X - formattedText.Width / 2,
            TextAlignment.Right => origin.X - formattedText.Width,
            _ => origin.X
        };
        drawingContext.DrawText(formattedText, new Point(x, origin.Y));
    }

    private Brush FindBrush(string resourceKey, Brush defaultBrush) =>
        TryFindResource(resourceKey) as Brush ?? defaultBrush;

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

    private static Brush CreateBrush(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }
}
