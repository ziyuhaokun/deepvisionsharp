using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Shell.ViewModels.Gallery;

namespace DeepVisionSharp.Shell.Rendering;

internal static class ProjectLabelStatisticsChartRenderer
{
    private const string OtherLabel = "其他";


    public static ImageSource? Render(ProjectLabelStatisticsChartRequest request)
    {
        if (request.Rows.Count == 0)
        {
            return null;
        }

        try
        {
            return request.ChartType switch
            {
                ProjectLabelChartType.StackedByGallery => RenderStackedByGallery(request),
                ProjectLabelChartType.Pie => RenderPie(request, isDonut: false),
                ProjectLabelChartType.Donut => RenderPie(request, isDonut: true),
                _ => RenderHorizontalBar(request),
            };
        }
        catch (Exception)
        {
            // Keep the statistics dialog alive if a chart type hits a managed rendering fault.
            // StackOverflow / native milcore faults still cannot be caught here.
            return null;
        }
    }

    private static ImageSource? RenderHorizontalBar(ProjectLabelStatisticsChartRequest request)
    {
        var titleBrush = ResolveBrush(request.TitleBrush, 240, 240, 240);
        var textBrush = ResolveBrush(request.TextBrush, 180, 180, 180);

        int displayCount = ResolveDisplayCount(request.Rows.Count, request.TopCount);
        var chartRows = request.Rows.Take(displayCount).ToArray();
        int max = Math.Max(1, chartRows.Max(row => row.SampleCount));
        int totalSamples = request.Rows.Sum(row => row.SampleCount);

        const int padding = 20;
        const int labelWidth = 196;
        const int valueWidth = 88;
        const int barHeight = 16;
        const int rowGap = 12;
        const int headerHeight = 42;
        const int axisHeight = 28;

        int width = ResolveWidth(request.ContainerWidth);
        int plotWidth = Math.Max(220, width - padding * 2 - labelWidth - valueWidth);
        int height = padding * 2 + headerHeight + chartRows.Length * (barHeight + rowGap) + axisHeight;

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            DrawCardBackground(dc, width, height, textBrush);
            DrawHeader(dc, "标签样本分布", BuildSubtitle(displayCount, request.TotalLabelCount, request.TopCount), padding, width, titleBrush, textBrush, request.Dpi);

            int plotLeft = padding + labelWidth;
            int plotTop = padding + headerHeight;
            int plotBottom = plotTop + chartRows.Length * (barHeight + rowGap) - rowGap;
            var gridBrush = CreateOverlayBrush(textBrush, 28);
            var trackBrush = CreateOverlayBrush(textBrush, 36);

            DrawGridLines(dc, gridBrush, plotLeft, plotTop, plotBottom, plotWidth, max);

            for (int i = 0; i < chartRows.Length; i++)
            {
                var row = chartRows[i];
                int y = plotTop + i * (barHeight + rowGap);
                double ratio = Math.Clamp((double)row.SampleCount / max, 0, 1);
                double percent = totalSamples > 0 ? (double)row.SampleCount / totalSamples : 0;

                DrawText(dc, row.Label, padding, padding + labelWidth - 8, y, titleBrush, 12.5, FontWeights.Normal, request.Dpi);
                dc.DrawRoundedRectangle(trackBrush, null, new Rect(plotLeft, y, plotWidth, barHeight), 8, 8);
                dc.DrawRoundedRectangle(CreateBarBrush(i), null, new Rect(plotLeft, y, Math.Max(6, plotWidth * ratio), barHeight), 8, 8);
                DrawText(dc, $"{row.SampleCount:N0} ({percent:P1})", plotLeft + plotWidth + 10, width - padding, y, textBrush, 11.5, FontWeights.Normal, request.Dpi);
            }

            DrawAxisLabels(dc, textBrush, plotLeft, plotBottom + 10, plotWidth, max, request.Dpi);
        }

        return RenderBitmap(visual, width, height, request.Dpi);
    }

    private static ImageSource? RenderStackedByGallery(ProjectLabelStatisticsChartRequest request)
    {
        if (request.GalleryRows.Count == 0)
        {
            return null;
        }

        var titleBrush = ResolveBrush(request.TitleBrush, 240, 240, 240);
        var textBrush = ResolveBrush(request.TextBrush, 180, 180, 180);
        var slices = BuildPieSlices(request.Rows, request.TopCount);
        var labelOrder = slices.Select(slice => slice.Label).ToArray();
        var labelColorMap = BuildLabelColorMap(labelOrder);
        var topLabelSet = new HashSet<string>(
            labelOrder.Where(label => !string.Equals(label, OtherLabel, StringComparison.OrdinalIgnoreCase)),
            StringComparer.OrdinalIgnoreCase);

        var galleries = request.GalleryRows
            .GroupBy(row => (row.GalleryId, row.GalleryName))
            .OrderBy(group => group.Key.GalleryName, StringComparer.OrdinalIgnoreCase)
            .Select(group => new GalleryStackData(
                group.Key.GalleryId,
                group.Key.GalleryName,
                group.ToDictionary(item => item.Label, item => item.SampleCount, StringComparer.OrdinalIgnoreCase)))
            .ToArray();

        int displayCount = ResolveDisplayCount(galleries.Length, request.TopCount);
        galleries = galleries.Take(displayCount).ToArray();
        int maxTotal = Math.Max(1, galleries.Max(gallery => SumGalleryCounts(gallery, labelOrder, topLabelSet)));

        const int padding = 20;
        const int headerHeight = 72;
        const int legendHeight = 28;
        const int axisHeight = 28;
        const int columnGap = 18;
        const int plotHeight = 220;
        const int labelAreaHeight = 42;

        int width = ResolveWidth(request.ContainerWidth);
        int plotLeft = padding + 36;
        int plotRight = width - padding;
        int plotWidth = plotRight - plotLeft;
        int columnWidth = galleries.Length > 0
            ? Math.Clamp((plotWidth - columnGap * (galleries.Length - 1)) / galleries.Length, 48, 120)
            : 80;
        int height = padding * 2 + headerHeight + legendHeight + plotHeight + labelAreaHeight + axisHeight;

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            DrawCardBackground(dc, width, height, textBrush);
            DrawHeader(dc, "按图库堆叠分布", BuildSubtitle(displayCount, request.GalleryRows.Select(r => r.GalleryId).Distinct().Count(), request.TopCount, "图库"), padding, width, titleBrush, textBrush, request.Dpi);
            DrawLegend(dc, labelOrder, labelColorMap, padding, padding + headerHeight, width - padding * 2, textBrush, request.Dpi);

            int plotTop = padding + headerHeight + legendHeight;
            int plotBottom = plotTop + plotHeight;
            var gridBrush = CreateOverlayBrush(textBrush, 28);
            var trackBrush = CreateOverlayBrush(textBrush, 36);
            DrawHorizontalGridLines(dc, gridBrush, plotLeft, plotTop, plotBottom, plotWidth, maxTotal);

            for (int i = 0; i < galleries.Length; i++)
            {
                var gallery = galleries[i];
                double x = plotLeft + i * (columnWidth + columnGap);
                double y = plotBottom;
                double usedHeight = 0;

                foreach (var label in labelOrder)
                {
                    int count = ResolveGalleryLabelCount(gallery, label, topLabelSet);
                    if (count <= 0)
                    {
                        continue;
                    }

                    double segmentHeight = plotHeight * count / maxTotal;
                    y -= segmentHeight;
                    dc.DrawRectangle(labelColorMap[label], null, new Rect(x, y, columnWidth, segmentHeight));
                    usedHeight += segmentHeight;
                }

                if (usedHeight < 2)
                {
                    dc.DrawRectangle(trackBrush, null, new Rect(x, plotBottom - 2, columnWidth, 2));
                }

                int total = SumGalleryCounts(gallery, labelOrder, topLabelSet);
                DrawText(dc, gallery.GalleryName, x - 8, x + columnWidth + 8, plotBottom + 8, textBrush, 11, FontWeights.Normal, request.Dpi, TextAlignment.Center);
                DrawText(dc, total.ToString("N0", CultureInfo.CurrentCulture), x - 8, x + columnWidth + 8, plotBottom + 24, titleBrush, 11.5, FontWeights.SemiBold, request.Dpi, TextAlignment.Center);
            }

            DrawVerticalAxisLabels(dc, textBrush, padding, plotTop, plotBottom, maxTotal, request.Dpi);
        }

        return RenderBitmap(visual, width, height, request.Dpi);
    }

    private static ImageSource? RenderPie(ProjectLabelStatisticsChartRequest request, bool isDonut)
    {
        var titleBrush = ResolveBrush(request.TitleBrush, 240, 240, 240);
        var textBrush = ResolveBrush(request.TextBrush, 180, 180, 180);
        var slices = BuildPieSlices(request.Rows, request.TopCount);
        if (slices.Count == 0)
        {
            return null;
        }

        const int padding = 20;
        const int headerHeight = 42;
        const int legendWidth = 220;
        int width = ResolveWidth(request.ContainerWidth);
        int height = Math.Max(360, padding * 2 + headerHeight + 300);

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            DrawCardBackground(dc, width, height, textBrush);
            DrawHeader(dc, isDonut ? "标签环形分布" : "标签饼图分布", BuildSubtitle(slices.Count, request.TotalLabelCount, request.TopCount), padding, width, titleBrush, textBrush, request.Dpi);

            double chartTop = padding + headerHeight;
            double availableHeight = height - chartTop - padding;
            double chartSize = Math.Min(280, availableHeight);
            double contentWidth = chartSize + padding + legendWidth;
            double contentLeft = Math.Max(padding, (width - contentWidth) / 2);
            double chartLeft = contentLeft;
            var center = new System.Windows.Point(chartLeft + chartSize / 2, chartTop + chartSize / 2);
            double outerRadius = chartSize / 2 - 8;
            double innerRadius = isDonut ? outerRadius * 0.58 : 0;

            double startAngle = -90;
            int total = slices.Sum(slice => slice.SampleCount);
            for (int i = 0; i < slices.Count; i++)
            {
                var slice = slices[i];
                double sweep = total > 0 ? 360d * slice.SampleCount / total : 0;
                if (sweep <= 0.01)
                {
                    continue;
                }

                var brush = new SolidColorBrush(ChartPalette.Series[i % ChartPalette.Series.Count]);
                brush.Freeze();
                DrawPieSlice(dc, center, outerRadius, innerRadius, startAngle, sweep, brush);
                startAngle += sweep;
            }

            if (isDonut)
            {
                DrawText(dc, total.ToString("N0", CultureInfo.CurrentCulture), center.X - 48, center.X + 48, center.Y - 14, titleBrush, 20, FontWeights.SemiBold, request.Dpi, TextAlignment.Center);
                DrawText(dc, "样本总计", center.X - 48, center.X + 48, center.Y + 10, textBrush, 11, FontWeights.Normal, request.Dpi, TextAlignment.Center);
            }

            double legendX = chartLeft + chartSize + padding;
            double legendHeight = slices.Count * 24;
            double legendY = chartTop + Math.Max(0, (chartSize - legendHeight) / 2);
            for (int i = 0; i < slices.Count; i++)
            {
                var slice = slices[i];
                double percent = total > 0 ? (double)slice.SampleCount / total : 0;
                var color = ChartPalette.Series[i % ChartPalette.Series.Count];
                dc.DrawRoundedRectangle(new SolidColorBrush(color), null, new Rect(legendX, legendY + i * 24, 12, 12), 3, 3);
                DrawText(dc, $"{slice.Label}  {slice.SampleCount:N0} ({percent:P1})", legendX + 18, width - padding, legendY + i * 24 - 1, textBrush, 11.5, FontWeights.Normal, request.Dpi);
            }
        }

        return RenderBitmap(visual, width, height, request.Dpi);
    }

    private static IReadOnlyList<PieSlice> BuildPieSlices(IReadOnlyList<ProjectLabelCountRow> rows, int topCount)
    {
        int displayCount = ResolveDisplayCount(rows.Count, topCount);
        var selected = rows.Take(displayCount).ToArray();
        if (displayCount >= rows.Count)
        {
            return selected.Select(row => new PieSlice(row.Label, row.SampleCount)).ToArray();
        }

        int otherCount = rows.Skip(displayCount).Sum(row => row.SampleCount);
        var slices = selected.Select(row => new PieSlice(row.Label, row.SampleCount)).ToList();
        if (otherCount > 0)
        {
            slices.Add(new PieSlice(OtherLabel, otherCount));
        }

        return slices;
    }

    private static Dictionary<string, System.Windows.Media.Brush> BuildLabelColorMap(IReadOnlyList<string> labels)
    {
        var map = new Dictionary<string, System.Windows.Media.Brush>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < labels.Count; i++)
        {
            var brush = new SolidColorBrush(ChartPalette.Series[i % ChartPalette.Series.Count]);
            brush.Freeze();
            map[labels[i]] = brush;
        }

        return map;
    }

    private static int ResolveGalleryLabelCount(
        GalleryStackData gallery,
        string label,
        IReadOnlySet<string> topLabelSet)
    {
        if (string.Equals(label, OtherLabel, StringComparison.OrdinalIgnoreCase))
        {
            return gallery.Counts
                .Where(pair => !topLabelSet.Contains(pair.Key))
                .Sum(pair => pair.Value);
        }

        return gallery.Counts.TryGetValue(label, out var count) ? count : 0;
    }

    private static int SumGalleryCounts(
        GalleryStackData gallery,
        IReadOnlyList<string> labels,
        IReadOnlySet<string> topLabelSet)
    {
        int total = 0;
        foreach (var label in labels)
        {
            total += ResolveGalleryLabelCount(gallery, label, topLabelSet);
        }

        return total;
    }

    private static void DrawPieSlice(
        DrawingContext dc,
        System.Windows.Point center,
        double outerRadius,
        double innerRadius,
        double startAngle,
        double sweepAngle,
        System.Windows.Media.Brush brush)
    {
        if (outerRadius <= 0 || sweepAngle <= 0)
        {
            return;
        }

        // Full circle: never feed ArcTo a 360° sweep (WPF/milcore can AV), and never
        // recurse into this method with 359.99 — that used to stack-overflow and kill the process
        // with no managed exception, no Event Log entry, and often no LocalDump.
        if (sweepAngle >= 359.99)
        {
            if (innerRadius <= 0)
            {
                dc.DrawEllipse(brush, null, center, outerRadius, outerRadius);
                return;
            }

            double hole = Math.Clamp(innerRadius, 0, outerRadius * 0.99);
            var ring = new GeometryGroup { FillRule = FillRule.EvenOdd };
            ring.Children.Add(new EllipseGeometry(center, outerRadius, outerRadius));
            if (hole > 0)
            {
                ring.Children.Add(new EllipseGeometry(center, hole, hole));
            }

            ring.Freeze();
            dc.DrawGeometry(brush, null, ring);
            return;
        }

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            System.Windows.Point outerStart = PolarToPoint(center, outerRadius, startAngle);
            System.Windows.Point outerEnd = PolarToPoint(center, outerRadius, startAngle + sweepAngle);

            if (innerRadius <= 0)
            {
                ctx.BeginFigure(center, true, true);
                ctx.LineTo(outerStart, true, false);
                ctx.ArcTo(outerEnd, new System.Windows.Size(outerRadius, outerRadius), 0, sweepAngle > 180, SweepDirection.Clockwise, true, false);
            }
            else
            {
                double hole = Math.Min(innerRadius, outerRadius * 0.99);
                System.Windows.Point innerEnd = PolarToPoint(center, hole, startAngle + sweepAngle);
                System.Windows.Point innerStart = PolarToPoint(center, hole, startAngle);
                ctx.BeginFigure(outerStart, true, true);
                ctx.ArcTo(outerEnd, new System.Windows.Size(outerRadius, outerRadius), 0, sweepAngle > 180, SweepDirection.Clockwise, true, false);
                ctx.LineTo(innerEnd, true, false);
                ctx.ArcTo(innerStart, new System.Windows.Size(hole, hole), 0, sweepAngle > 180, SweepDirection.Counterclockwise, true, false);
            }
        }

        geometry.Freeze();
        dc.DrawGeometry(brush, null, geometry);
    }

    private static System.Windows.Point PolarToPoint(System.Windows.Point center, double radius, double angleDegrees)
    {
        double radians = angleDegrees * Math.PI / 180d;
        return new System.Windows.Point(
            center.X + radius * Math.Cos(radians),
            center.Y + radius * Math.Sin(radians));
    }

    private static void DrawLegend(
        DrawingContext dc,
        IReadOnlyList<string> labels,
        IReadOnlyDictionary<string, System.Windows.Media.Brush> colors,
        double x,
        double y,
        double maxWidth,
        System.Windows.Media.Brush textBrush,
        DpiScale dpi)
    {
        double cursorX = x;
        double cursorY = y;
        const double itemGap = 16;
        const double rowHeight = 18;

        for (int i = 0; i < labels.Count; i++)
        {
            string label = labels[i];
            dc.DrawRoundedRectangle(colors[label], null, new Rect(cursorX, cursorY + 3, 10, 10), 2, 2);
            var formatted = CreateFormattedText(label, textBrush, 11, FontWeights.Normal, dpi);
            double itemWidth = 14 + formatted.Width + itemGap;
            if (cursorX + itemWidth > x + maxWidth && cursorX > x)
            {
                cursorX = x;
                cursorY += rowHeight;
            }

            DrawText(dc, label, cursorX + 14, cursorX + 160, cursorY, textBrush, 11, FontWeights.Normal, dpi);
            cursorX += itemWidth;
        }
    }

    private static void DrawCardBackground(DrawingContext dc, int width, int height, System.Windows.Media.Brush themeBrush)
    {
        var backgroundBrush = CreateOverlayBrush(themeBrush, 18);
        var borderBrush = CreateOverlayBrush(themeBrush, 48);
        dc.DrawRoundedRectangle(backgroundBrush, new System.Windows.Media.Pen(borderBrush, 1), new Rect(0, 0, width, height), 12, 12);
    }

    /// <summary>
    /// Derives a low-alpha overlay from the theme's text color so grid lines, tracks and the card
    /// surface invert with the theme instead of assuming a dark background.
    /// </summary>
    private static System.Windows.Media.Brush CreateOverlayBrush(System.Windows.Media.Brush themeBrush, byte alpha)
    {
        var baseColor = themeBrush is SolidColorBrush solid ? solid.Color : Colors.White;
        var brush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B));
        brush.Freeze();
        return brush;
    }

    private static void DrawHeader(
        DrawingContext dc,
        string title,
        string subtitle,
        int padding,
        int width,
        System.Windows.Media.Brush titleBrush,
        System.Windows.Media.Brush textBrush,
        DpiScale dpi)
    {
        DrawText(dc, title, padding, width - padding, padding, titleBrush, 15, FontWeights.SemiBold, dpi);
        DrawText(dc, subtitle, padding, width - padding, padding + 22, textBrush, 12, FontWeights.Normal, dpi);
    }

    private static string BuildSubtitle(int displayCount, int totalCount, int topCount, string unit = "项")
        => topCount <= 0
            ? $"展示全部 {displayCount} {unit} / 共 {totalCount} {unit}"
            : $"展示 Top {displayCount} / 共 {totalCount} {unit}";

    private static int ResolveDisplayCount(int totalCount, int topCount)
        => topCount <= 0 ? totalCount : Math.Min(topCount, totalCount);

    private static int ResolveWidth(double containerWidth)
        => (int)Math.Clamp(containerWidth > 0 ? containerWidth : 900, 640, 1200);

    private static System.Windows.Media.Brush ResolveBrush(System.Windows.Media.Brush? brush, byte r, byte g, byte b)
        => brush ?? new SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));

    private static LinearGradientBrush CreateBarBrush(int index)
    {
        var color = ChartPalette.Series[index % ChartPalette.Series.Count];
        var brush = new LinearGradientBrush(Lighten(color, 0.12), color, new System.Windows.Point(0, 0.5), new System.Windows.Point(1, 0.5));
        brush.Freeze();
        return brush;
    }

    private static void DrawGridLines(
        DrawingContext dc,
        System.Windows.Media.Brush gridBrush,
        int plotLeft,
        int plotTop,
        int plotBottom,
        int plotWidth,
        int max)
    {
        foreach (var tick in BuildTicks(max))
        {
            double x = plotLeft + plotWidth * tick.Ratio;
            var pen = new System.Windows.Media.Pen(gridBrush, 1) { DashStyle = DashStyles.Dash };
            dc.DrawLine(pen, new System.Windows.Point(x, plotTop - 4), new System.Windows.Point(x, plotBottom + 4));
        }
    }

    private static void DrawHorizontalGridLines(
        DrawingContext dc,
        System.Windows.Media.Brush gridBrush,
        int plotLeft,
        int plotTop,
        int plotBottom,
        int plotWidth,
        int max)
    {
        foreach (var tick in BuildTicks(max))
        {
            double y = plotBottom - (plotBottom - plotTop) * tick.Ratio;
            var pen = new System.Windows.Media.Pen(gridBrush, 1) { DashStyle = DashStyles.Dash };
            dc.DrawLine(pen, new System.Windows.Point(plotLeft, y), new System.Windows.Point(plotLeft + plotWidth, y));
        }
    }

    private static void DrawAxisLabels(
        DrawingContext dc,
        System.Windows.Media.Brush textBrush,
        int plotLeft,
        int y,
        int plotWidth,
        int max,
        DpiScale dpi)
    {
        foreach (var tick in BuildTicks(max))
        {
            double x = plotLeft + plotWidth * tick.Ratio;
            DrawText(dc, tick.Label, x - 28, x + 28, y, textBrush, 10.5, FontWeights.Normal, dpi, TextAlignment.Center);
        }
    }

    private static void DrawVerticalAxisLabels(
        DrawingContext dc,
        System.Windows.Media.Brush textBrush,
        int x,
        int plotTop,
        int plotBottom,
        int max,
        DpiScale dpi)
    {
        foreach (var tick in BuildTicks(max))
        {
            double y = plotBottom - (plotBottom - plotTop) * tick.Ratio;
            DrawText(dc, tick.Label, 0, x - 6, y - 8, textBrush, 10.5, FontWeights.Normal, dpi, TextAlignment.Right);
        }
    }

    private static IReadOnlyList<ChartTick> BuildTicks(int max)
    {
        if (max <= 1)
        {
            return [new ChartTick(0, "0"), new ChartTick(1, "1")];
        }

        return
        [
            new ChartTick(0, "0"),
            new ChartTick(0.25, FormatTick(max * 0.25)),
            new ChartTick(0.5, FormatTick(max * 0.5)),
            new ChartTick(0.75, FormatTick(max * 0.75)),
            new ChartTick(1, FormatTick(max)),
        ];
    }

    private static string FormatTick(double value)
        => ((int)Math.Round(value)).ToString("N0", CultureInfo.CurrentCulture);

    private static System.Windows.Media.Color Lighten(System.Windows.Media.Color color, double amount)
    {
        byte L(byte channel) => (byte)Math.Clamp(channel + (255 - channel) * amount, 0, 255);
        return System.Windows.Media.Color.FromRgb(L(color.R), L(color.G), L(color.B));
    }

    private static FormattedText CreateFormattedText(
        string text,
        System.Windows.Media.Brush brush,
        double fontSize,
        FontWeight weight,
        DpiScale dpi)
        => new(
            text,
            CultureInfo.CurrentUICulture,
            System.Windows.FlowDirection.LeftToRight,
            new Typeface(new System.Windows.Media.FontFamily("Segoe UI"), FontStyles.Normal, weight, FontStretches.Normal),
            fontSize,
            brush,
            dpi.PixelsPerDip);

    private static void DrawText(
        DrawingContext dc,
        string text,
        double xLeft,
        double xRight,
        double y,
        System.Windows.Media.Brush brush,
        double fontSize,
        FontWeight weight,
        DpiScale dpi,
        TextAlignment alignment = TextAlignment.Left)
    {
        var formatted = CreateFormattedText(text, brush, fontSize, weight, dpi);
        formatted.MaxTextWidth = Math.Max(20, xRight - xLeft);
        formatted.Trimming = TextTrimming.CharacterEllipsis;
        formatted.TextAlignment = alignment;
        dc.DrawText(formatted, new System.Windows.Point(xLeft, y));
    }

    private static ImageSource RenderBitmap(DrawingVisual visual, int width, int height, DpiScale dpi)
    {
        var bitmap = new RenderTargetBitmap(
            (int)(width * dpi.DpiScaleX),
            (int)(height * dpi.DpiScaleY),
            dpi.PixelsPerInchX,
            dpi.PixelsPerInchY,
            PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private readonly record struct ChartTick(double Ratio, string Label);
    private readonly record struct PieSlice(string Label, int SampleCount);
    private readonly record struct GalleryStackData(Guid GalleryId, string GalleryName, Dictionary<string, int> Counts);
}

internal sealed record ProjectLabelStatisticsChartRequest(
    ProjectLabelChartType ChartType,
    IReadOnlyList<ProjectLabelCountRow> Rows,
    IReadOnlyList<ProjectGalleryLabelCountRow> GalleryRows,
    int TopCount,
    int TotalLabelCount,
    double ContainerWidth,
    DpiScale Dpi,
    System.Windows.Media.Brush? TitleBrush,
    System.Windows.Media.Brush? TextBrush);
