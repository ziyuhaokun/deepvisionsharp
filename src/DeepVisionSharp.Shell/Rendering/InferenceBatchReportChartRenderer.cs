using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DeepVisionSharp.Shell.ViewModels.Inference;

namespace DeepVisionSharp.Shell.Rendering;

/// <summary>
/// Draws static chart bitmaps for the batch inference analysis report dialog.
/// </summary>
internal static class InferenceBatchReportChartRenderer
{
    private const int MaxLatencyBars = 48;


    public static InferenceBatchReportChartImages RenderAll(
        InferenceBatchReport report,
        InferenceBatchReportChartTheme theme)
    {
        return new InferenceBatchReportChartImages(
            RenderConfusionMatrix(report.Evaluation, theme),
            RenderRateBars(report.Evaluation, theme),
            RenderSuccessFail(report.SuccessCount, report.FailedCount, theme),
            RenderLatencyBars(report, theme));
    }

    public static ImageSource RenderConfusionMatrix(
        InferenceOkNgEvaluation evaluation,
        InferenceBatchReportChartTheme theme)
    {
        const int width = 420;
        const int height = 220;
        if (evaluation.PairedCount <= 0)
            return RenderEmptyChart(width, height, "OK/NG 子集混淆矩阵", "NG 为正类", "无 OK/NG 成对样本", theme);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            DrawCardBackground(dc, width, height, theme);
            // Keep subtitle short — row/col headers already encode axes (no overlap with them).
            DrawHeader(
                dc,
                "混淆矩阵",
                evaluation.PairedCount <= 0
                    ? "NG 为正类"
                    : $"NG 为正类 · 成对 {evaluation.PairedCount}",
                12,
                width,
                theme);

            var maxCell = Math.Max(
                1,
                Math.Max(
                    Math.Max(evaluation.TruePositive, evaluation.FalseNegative),
                    Math.Max(evaluation.FalsePositive, evaluation.TrueNegative)));

            // Layout: title(0–14) + subtitle(20–34) + gap + col headers(46–60) + gap + cells(66+)
            const double left = 72;
            const double colHeaderY = 48;
            const double top = 68;
            const double cellW = 118;
            const double cellH = 54;
            const double gap = 8;
            const double rowLabelRight = left - 10;

            // Column headers (below subtitle, above cells)
            DrawText(
                dc,
                "预测 NG",
                left,
                left + cellW,
                colHeaderY,
                theme.TextBrush,
                11,
                FontWeights.SemiBold,
                theme.Dpi,
                TextAlignment.Center);
            DrawText(
                dc,
                "预测 OK",
                left + cellW + gap,
                left + cellW * 2 + gap,
                colHeaderY,
                theme.TextBrush,
                11,
                FontWeights.SemiBold,
                theme.Dpi,
                TextAlignment.Center);

            // Row headers (right-aligned in left gutter)
            DrawText(
                dc,
                "实际 NG",
                6,
                rowLabelRight,
                top + cellH * 0.5 - 8,
                theme.TextBrush,
                11,
                FontWeights.SemiBold,
                theme.Dpi,
                TextAlignment.Right);
            DrawText(
                dc,
                "实际 OK",
                6,
                rowLabelRight,
                top + cellH + gap + cellH * 0.5 - 8,
                theme.TextBrush,
                11,
                FontWeights.SemiBold,
                theme.Dpi,
                TextAlignment.Right);

            DrawMatrixCell(dc, left, top, cellW, cellH, evaluation.TruePositive, maxCell, "TP", ChartPalette.Success, theme);
            DrawMatrixCell(dc, left + cellW + gap, top, cellW, cellH, evaluation.FalseNegative, maxCell, "FN 漏检", ChartPalette.Miss, theme);
            DrawMatrixCell(dc, left, top + cellH + gap, cellW, cellH, evaluation.FalsePositive, maxCell, "FP 误报", ChartPalette.Warning, theme);
            DrawMatrixCell(dc, left + cellW + gap, top + cellH + gap, cellW, cellH, evaluation.TrueNegative, maxCell, "TN", ChartPalette.Neutral, theme);
        }

        return RenderBitmap(visual, width, height, theme.Dpi);
    }

    public static ImageSource RenderRateBars(
        InferenceOkNgEvaluation evaluation,
        InferenceBatchReportChartTheme theme)
    {
        const int width = 420;
        const int height = 200;
        if (evaluation.ClassifiedCount <= 0)
            return RenderEmptyChart(width, height, "分类评估率", "成对样本", "需预计 + 推理标记", theme);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            DrawCardBackground(dc, width, height, theme);
            DrawHeader(dc, "分类评估率", "漏检 / 误报仅统计 OK/NG 子集", 12, width, theme);

            var rows = evaluation.PairedCount > 0
                ? new (string Label, double Rate, Color Color)[]
                {
                    ("漏检率", evaluation.MissRate, ChartPalette.Miss),
                    ("误报率", evaluation.FalseAlarmRate, ChartPalette.Warning),
                    ("准确率", evaluation.ClassificationAccuracy, ChartPalette.Success)
                }
                : new (string Label, double Rate, Color Color)[] { ("准确率", evaluation.ClassificationAccuracy, ChartPalette.Success) };

            const double padding = 14;
            const double labelW = 56;
            const double valueW = 52;
            const double barH = 18;
            const double rowGap = 28;
            double plotLeft = padding + labelW;
            double plotWidth = width - padding * 2 - labelW - valueW;
            double y = 56;

            // 0–100% axis ticks
            for (var i = 0; i <= 4; i++)
            {
                double ratio = i / 4.0;
                double x = plotLeft + plotWidth * ratio;
                var pen = new Pen(theme.GridBrush, 1) { DashStyle = DashStyles.Dash };
                pen.Freeze();
                dc.DrawLine(pen, new Point(x, 50), new Point(x, y + rows.Length * rowGap - 8));
                DrawText(
                    dc,
                    $"{ratio:P0}",
                    x - 18,
                    x + 18,
                    y + rows.Length * rowGap - 4,
                    theme.TextBrush,
                    10,
                    FontWeights.Normal,
                    theme.Dpi,
                    TextAlignment.Center);
            }

            foreach (var row in rows)
            {
                DrawText(dc, row.Label, padding, plotLeft - 6, y + 1, theme.TextBrush, 12, FontWeights.Normal, theme.Dpi, TextAlignment.Right);
                double barWidth = Math.Max(2, plotWidth * Math.Clamp(row.Rate, 0, 1));
                var brush = CreateSolid(row.Color, 0.92);
                dc.DrawRoundedRectangle(brush, null, new Rect(plotLeft, y, barWidth, barH), 4, 4);
                DrawText(
                    dc,
                    $"{row.Rate:P1}",
                    plotLeft + plotWidth + 4,
                    width - padding,
                    y + 1,
                    theme.TitleBrush,
                    12,
                    FontWeights.SemiBold,
                    theme.Dpi);
                y += rowGap;
            }
        }

        return RenderBitmap(visual, width, height, theme.Dpi);
    }

    public static ImageSource RenderSuccessFail(
        int successCount,
        int failedCount,
        InferenceBatchReportChartTheme theme)
    {
        const int width = 420;
        const int height = 200;
        var total = successCount + failedCount;
        if (total <= 0) return RenderEmptyChart(width, height, "运行结果", "无样本", "暂无运行数据", theme);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            DrawCardBackground(dc, width, height, theme);
            DrawHeader(dc, "运行结果", total <= 0 ? "无样本" : $"共 {total} 张 · 成功 {successCount} · 失败 {failedCount}", 12, width, theme);

            var center = new Point(120, 118);
            const double radius = 58;
            double successAngle = 360.0 * successCount / total;
            double failAngle = 360.0 - successAngle;

            if (successCount > 0)
            {
                DrawPieSlice(dc, center, radius, -90, successAngle, CreateSolid(ChartPalette.Success, 0.95));
            }

            if (failedCount > 0)
            {
                DrawPieSlice(dc, center, radius, -90 + successAngle, failAngle, CreateSolid(ChartPalette.Critical, 0.95));
            }

            // Donut hole
            dc.DrawEllipse(theme.CardFillBrush, null, center, radius * 0.52, radius * 0.52);
            DrawText(
                dc,
                total <= 0 ? "—" : $"{(double)successCount / total:P0}",
                center.X - 36,
                center.X + 36,
                center.Y - 10,
                theme.TitleBrush,
                16,
                FontWeights.SemiBold,
                theme.Dpi,
                TextAlignment.Center);

            // Legend
            double lx = 210;
            double ly = 72;
            DrawLegendItem(dc, lx, ly, ChartPalette.Success, $"成功  {successCount}", theme);
            DrawLegendItem(dc, lx, ly + 28, ChartPalette.Critical, $"失败  {failedCount}", theme);
        }

        return RenderBitmap(visual, width, height, theme.Dpi);
    }

    public static ImageSource RenderLatencyBars(
        InferenceBatchReport report,
        InferenceBatchReportChartTheme theme)
    {
        const int width = 420;
        const int height = 200;
        var points = report.Samples.Where(s => s.IsSuccess && s.ElapsedMs is > 0)
            .Select(s => s.ElapsedMs!.Value).ToList();
        if (points.Count == 0)
            return RenderEmptyChart(width, height, "推理耗时", "成功样本", "暂无成功耗时", theme);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            DrawCardBackground(dc, width, height, theme);

            var display = points;
            var subtitle = $"成功 {points.Count} 张 · 均 {report.AverageSuccessMilliseconds:F1} ms";
            if (points.Count > MaxLatencyBars)
            {
                display = SampleEvenly(points, MaxLatencyBars);
                subtitle += $" · 显示 {display.Count}/{points.Count}";
            }

            DrawHeader(dc, "推理耗时", subtitle, 12, width, theme);

            const double padL = 40;
            const double padR = 14;
            const double padT = 52;
            const double padB = 28;
            double plotW = width - padL - padR;
            double plotH = height - padT - padB;
            double maxMs = Math.Max(1, display.Max());
            // pad top of scale a bit
            maxMs *= 1.08;

            // grid + y labels
            for (var i = 0; i <= 4; i++)
            {
                double ratio = i / 4.0;
                double y = padT + plotH * (1 - ratio);
                var pen = new Pen(theme.GridBrush, 1) { DashStyle = DashStyles.Dash };
                pen.Freeze();
                dc.DrawLine(pen, new Point(padL, y), new Point(padL + plotW, y));
                DrawText(
                    dc,
                    FormatMs(maxMs * ratio),
                    2,
                    padL - 4,
                    y - 7,
                    theme.TextBrush,
                    10,
                    FontWeights.Normal,
                    theme.Dpi,
                    TextAlignment.Right);
            }

            // average line
            if (report.AverageSuccessMilliseconds > 0)
            {
                double avgY = padT + plotH * (1 - Math.Clamp(report.AverageSuccessMilliseconds / maxMs, 0, 1));
                var avgPen = new Pen(CreateSolid(ChartPalette.Warning, 0.9), 1.5) { DashStyle = DashStyles.Dash };
                avgPen.Freeze();
                dc.DrawLine(avgPen, new Point(padL, avgY), new Point(padL + plotW, avgY));
            }

            double barGap = display.Count <= 1 ? 0 : 2;
            double barW = Math.Max(2, (plotW - barGap * (display.Count - 1)) / display.Count);
            var barBrush = CreateSolid(ChartPalette.Accent, 0.9);
            for (var i = 0; i < display.Count; i++)
            {
                double ms = display[i];
                double h = plotH * Math.Clamp(ms / maxMs, 0, 1);
                double x = padL + i * (barW + barGap);
                double y = padT + plotH - h;
                dc.DrawRoundedRectangle(barBrush, null, new Rect(x, y, barW, Math.Max(1, h)), 2, 2);
            }

            DrawText(
                dc,
                "样本序",
                padL,
                padL + plotW,
                height - 18,
                theme.TextBrush,
                10,
                FontWeights.Normal,
                theme.Dpi,
                TextAlignment.Center);
        }

        return RenderBitmap(visual, width, height, theme.Dpi);
    }

    private static ImageSource RenderEmptyChart(int width, int height, string title, string subtitle,
        string message, InferenceBatchReportChartTheme theme)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            DrawCardBackground(dc, width, height, theme);
            DrawHeader(dc, title, subtitle, 12, width, theme);
            DrawEmpty(dc, width, height, message, theme);
        }
        // Close the drawing context before taking the bitmap, including empty states.
        return RenderBitmap(visual, width, height, theme.Dpi);
    }

    private static List<double> SampleEvenly(IReadOnlyList<double> source, int count)
    {
        if (source.Count <= count)
        {
            return source.ToList();
        }

        var result = new List<double>(count);
        for (var i = 0; i < count; i++)
        {
            int index = (int)Math.Round(i * (source.Count - 1.0) / (count - 1.0));
            result.Add(source[index]);
        }

        return result;
    }

    private static string FormatMs(double ms)
        => ms >= 100 ? $"{ms:F0}" : $"{ms:F1}";

    private static void DrawMatrixCell(
        DrawingContext dc,
        double x,
        double y,
        double w,
        double h,
        int count,
        int maxCell,
        string label,
        Color baseColor,
        InferenceBatchReportChartTheme theme)
    {
        double intensity = 0.25 + 0.7 * count / Math.Max(1, maxCell);
        var fill = CreateSolid(baseColor, intensity);
        var border = new Pen(CreateSolid(baseColor, 0.55), 1);
        border.Freeze();
        dc.DrawRoundedRectangle(fill, border, new Rect(x, y, w, h), 6, 6);
        DrawText(dc, count.ToString(CultureInfo.CurrentCulture), x, x + w, y + 8, theme.TitleBrush, 18, FontWeights.SemiBold, theme.Dpi, TextAlignment.Center);
        DrawText(dc, label, x, x + w, y + h - 22, theme.TextBrush, 11, FontWeights.Normal, theme.Dpi, TextAlignment.Center);
    }

    private static void DrawPieSlice(
        DrawingContext dc,
        Point center,
        double radius,
        double startDegrees,
        double sweepDegrees,
        Brush brush)
    {
        if (sweepDegrees <= 0)
        {
            return;
        }

        if (sweepDegrees >= 359.9)
        {
            dc.DrawEllipse(brush, null, center, radius, radius);
            return;
        }

        var start = Polar(center, radius, startDegrees);
        var end = Polar(center, radius, startDegrees + sweepDegrees);
        var figure = new PathFigure { StartPoint = center, IsClosed = true };
        figure.Segments.Add(new LineSegment(start, isStroked: false));
        figure.Segments.Add(new ArcSegment(
            end,
            new Size(radius, radius),
            0,
            sweepDegrees > 180,
            SweepDirection.Clockwise,
            isStroked: false));
        figure.Freeze();
        var geo = new PathGeometry([figure]);
        geo.Freeze();
        dc.DrawGeometry(brush, null, geo);
    }

    private static Point Polar(Point center, double radius, double degrees)
    {
        double rad = degrees * Math.PI / 180.0;
        return new Point(center.X + radius * Math.Cos(rad), center.Y + radius * Math.Sin(rad));
    }

    private static void DrawLegendItem(
        DrawingContext dc,
        double x,
        double y,
        Color color,
        string text,
        InferenceBatchReportChartTheme theme)
    {
        dc.DrawRoundedRectangle(CreateSolid(color, 0.95), null, new Rect(x, y + 3, 12, 12), 2, 2);
        DrawText(dc, text, x + 18, x + 180, y, theme.TitleBrush, 13, FontWeights.Normal, theme.Dpi);
    }

    private static void DrawCardBackground(
        DrawingContext dc,
        int width,
        int height,
        InferenceBatchReportChartTheme theme)
    {
        var border = new Pen(theme.BorderBrush, 1);
        border.Freeze();
        dc.DrawRoundedRectangle(theme.CardFillBrush, border, new Rect(0, 0, width, height), 10, 10);
    }

    private static void DrawHeader(
        DrawingContext dc,
        string title,
        string subtitle,
        int padding,
        int width,
        InferenceBatchReportChartTheme theme)
    {
        DrawText(dc, title, padding, width - padding, padding, theme.TitleBrush, 14, FontWeights.SemiBold, theme.Dpi);
        DrawText(dc, subtitle, padding, width - padding, padding + 20, theme.TextBrush, 11, FontWeights.Normal, theme.Dpi);
    }

    private static void DrawEmpty(
        DrawingContext dc,
        int width,
        int height,
        string message,
        InferenceBatchReportChartTheme theme)
    {
        DrawText(
            dc,
            message,
            20,
            width - 20,
            height * 0.5 - 6,
            theme.TextBrush,
            13,
            FontWeights.Normal,
            theme.Dpi,
            TextAlignment.Center);
    }

    private static SolidColorBrush CreateSolid(Color color, double opacity)
    {
        var brush = new SolidColorBrush(Color.FromArgb(
            (byte)Math.Clamp((int)(opacity * 255), 0, 255),
            color.R,
            color.G,
            color.B));
        brush.Freeze();
        return brush;
    }

    private static FormattedText CreateFormattedText(
        string text,
        Brush brush,
        double fontSize,
        FontWeight weight,
        DpiScale dpi)
        => new(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, weight, FontStretches.Normal),
            fontSize,
            brush,
            dpi.PixelsPerDip);

    private static void DrawText(
        DrawingContext dc,
        string text,
        double xLeft,
        double xRight,
        double y,
        Brush brush,
        double fontSize,
        FontWeight weight,
        DpiScale dpi,
        TextAlignment alignment = TextAlignment.Left)
    {
        var formatted = CreateFormattedText(text, brush, fontSize, weight, dpi);
        formatted.MaxTextWidth = Math.Max(20, xRight - xLeft);
        formatted.Trimming = TextTrimming.CharacterEllipsis;
        formatted.TextAlignment = alignment;
        dc.DrawText(formatted, new Point(xLeft, y));
    }

    private static ImageSource RenderBitmap(DrawingVisual visual, int width, int height, DpiScale dpi)
    {
        var bitmap = new RenderTargetBitmap(
            Math.Max(1, (int)(width * dpi.DpiScaleX)),
            Math.Max(1, (int)(height * dpi.DpiScaleY)),
            dpi.PixelsPerInchX,
            dpi.PixelsPerInchY,
            PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }
}

internal sealed record InferenceBatchReportChartImages(
    ImageSource ConfusionMatrix,
    ImageSource RateBars,
    ImageSource SuccessFail,
    ImageSource LatencyBars);

internal sealed record InferenceBatchReportChartTheme(
    DpiScale Dpi,
    Brush TitleBrush,
    Brush TextBrush,
    Brush GridBrush,
    Brush CardFillBrush,
    Brush BorderBrush);
