using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using DeepVisionSharp.Shell.ViewModels.Inference;

namespace DeepVisionSharp.Shell.Services.Reporting;

/// <summary>Builds a self-contained HTML document for batch inference report export.</summary>
public static class InferenceBatchReportHtmlBuilder
{
    public static string Build(InferenceBatchReport report)
    {
        var sb = new StringBuilder(96 * 1024);
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"zh-CN\">");
        sb.AppendLine("<head>");
        sb.AppendLine("<meta charset=\"utf-8\"/>");
        sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"/>");
        sb.AppendLine($"<title>{H(report.Headline)}</title>");
        sb.AppendLine("<style>");
        sb.AppendLine(Css);
        sb.AppendLine("</style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine("<div class=\"page\">");

        // Header
        sb.AppendLine("<header class=\"hero\">");
        sb.AppendLine("<div class=\"hero-top\">");
        sb.AppendLine("<span class=\"brand\">DeepVisionSharp</span>");
        sb.AppendLine("<span class=\"badge\">批量推理分析报告</span>");
        sb.AppendLine("</div>");
        sb.AppendLine($"<h1>{H(report.Headline)}</h1>");
        sb.AppendLine("<div class=\"meta-row\">");
        sb.AppendLine(MetaChip("时间", report.GeneratedAtText));
        sb.AppendLine(MetaChip("模型", report.ModelText));
        sb.AppendLine(MetaChip("设备", report.DeviceText));
        sb.AppendLine("</div>");
        sb.AppendLine("</header>");

        // Run overview metrics
        sb.AppendLine("<section class=\"panel\">");
        sb.AppendLine("<div class=\"panel-head\"><h2>运行概况</h2></div>");
        sb.AppendLine("<div class=\"metrics\">");
        sb.AppendLine(MetricCard("成功率", report.SuccessRateText, $"{report.SuccessCount}/{report.TotalCount} 成功", "accent"));
        sb.AppendLine(MetricCard("总耗时", report.TotalWallText, $"共 {report.TotalCount} 张", "neutral"));
        sb.AppendLine(MetricCard("成功均耗时", report.AverageSuccessText, report.MinMaxSuccessText, "neutral"));
        sb.AppendLine(MetricCard(
            "失败",
            report.FailedCount.ToString(CultureInfo.InvariantCulture),
            report.FailedCount > 0 ? "需检查失败原因" : "全部成功",
            report.FailedCount > 0 ? "danger" : "ok"));
        sb.AppendLine("</div>");
        sb.AppendLine("</section>");

        // Evaluation
        sb.AppendLine("<section class=\"panel\">");
        sb.AppendLine("<div class=\"panel-head\">");
        sb.AppendLine("<h2>分类评估</h2>");
        sb.AppendLine("<span class=\"panel-hint\">准确率统计全部类别；漏检、误报与混淆矩阵仅统计 OK/NG 子集</span>");
        sb.AppendLine("</div>");

        sb.AppendLine("<div class=\"metrics metrics-eval\">");
        sb.AppendLine(MetricCard("漏检率", report.MissRateText, "FN / (TP+FN)", "warn"));
        sb.AppendLine(MetricCard("误报率", report.FalseAlarmRateText, "FP / (TN+FP)", "violet"));
        sb.AppendLine(MetricCard("准确率", report.AccuracyText, "成对样本正确率", "ok"));
        sb.AppendLine(MetricCard(
            "成对样本",
            report.Evaluation.ClassifiedCount.ToString(CultureInfo.InvariantCulture),
            report.Evaluation.ClassifiedCount <= 0 ? "需预计 + 推理标记" : "已参与分类评估",
            "neutral"));
        sb.AppendLine("</div>");

        sb.AppendLine("<div class=\"confusion\">");
        sb.AppendLine("<div class=\"confusion-title\">OK/NG 子集混淆矩阵</div>");
        sb.AppendLine("<div class=\"confusion-grid\">");
        sb.AppendLine(ConfusionCell("TP", report.Evaluation.TruePositive, "命中 NG", "tp"));
        sb.AppendLine(ConfusionCell("FP", report.Evaluation.FalsePositive, "误报", "fp"));
        sb.AppendLine(ConfusionCell("FN", report.Evaluation.FalseNegative, "漏检", "fn"));
        sb.AppendLine(ConfusionCell("TN", report.Evaluation.TrueNegative, "正确 OK", "tn"));
        sb.AppendLine("</div>");
        sb.AppendLine($"<p class=\"confusion-note\">{H(report.ConfusionText)}</p>");
        sb.AppendLine("</div>");
        sb.AppendLine("</section>");

        // Original samples
        if (report.HasOriginalSamples)
        {
            sb.AppendLine("<section class=\"panel\">");
            sb.AppendLine("<div class=\"panel-head\">");
            sb.AppendLine("<h2>原图样例</h2>");
            sb.AppendLine($"<span class=\"panel-hint\">每类最多 {InferenceBatchReport.MaxOriginalSamplesPerRole} 张 · 不含文件名</span>");
            sb.AppendLine("</div>");
            AppendSampleGroup(sb, "OK 原图", report.OkOriginalSamples, "ok", "OK");
            AppendSampleGroup(sb, "NG 原图（命中）", report.NgOriginalSamples, "ng", "命中");
            AppendSampleGroup(sb, "漏检原图", report.MissOriginalSamples, "miss", "漏检");
            AppendSampleGroup(sb, "误报原图", report.FalseAlarmOriginalSamples, "false_alarm", "误报");
            sb.AppendLine("</section>");
        }

        // Anomalies
        sb.AppendLine("<section class=\"panel\">");
        sb.AppendLine("<div class=\"panel-head\">");
        sb.AppendLine("<h2>预测异常</h2>");
        sb.AppendLine($"<span class=\"panel-hint\">{H(report.AnomalySummaryText)}</span>");
        sb.AppendLine("</div>");

        if (report.AnomalyCount <= 0)
        {
            sb.AppendLine("<div class=\"empty\">");
            sb.AppendLine("<div class=\"empty-icon\">✓</div>");
            sb.AppendLine("<p>无预测异常</p>");
            sb.AppendLine("</div>");
        }
        else
        {
            sb.AppendLine("<div class=\"table-wrap\">");
            sb.AppendLine("<table>");
            sb.AppendLine("<thead><tr>");
            sb.AppendLine("<th>样本</th><th>来源</th><th>预计</th><th>推理</th><th>评估</th><th class=\"col-num\">耗时</th>");
            sb.AppendLine("</tr></thead><tbody>");
            foreach (var row in report.AnomalySamples)
            {
                var tagClass = string.Equals(row.EvaluationTag, "漏检", StringComparison.Ordinal)
                    ? "tag-miss"
                    : string.Equals(row.EvaluationTag, "误报", StringComparison.Ordinal)
                        ? "tag-fp"
                        : "tag-neutral";
                sb.AppendLine("<tr>");
                sb.AppendLine($"<td class=\"cell-name\">{H(row.DisplayName)}</td>");
                sb.AppendLine($"<td class=\"cell-muted\">{H(row.SourceLabel)}</td>");
                sb.AppendLine($"<td>{H(row.ExpectedText)}</td>");
                sb.AppendLine($"<td>{H(row.PredictedText)}</td>");
                sb.AppendLine($"<td><span class=\"tag {tagClass}\">{H(row.EvaluationTag)}</span></td>");
                sb.AppendLine($"<td class=\"col-num\">{H(row.ElapsedText)}</td>");
                sb.AppendLine("</tr>");
            }

            sb.AppendLine("</tbody></table>");
            sb.AppendLine("</div>");
        }

        sb.AppendLine("</section>");

        sb.AppendLine("<footer class=\"foot\">");
        sb.AppendLine("<span>DeepVisionSharp · 批量推理分析报告</span>");
        sb.AppendLine("<span>自包含 HTML · 可离线打开</span>");
        sb.AppendLine("</footer>");

        sb.AppendLine("</div>");
        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    private static void AppendSampleGroup(
        StringBuilder sb,
        string heading,
        IReadOnlyList<InferenceReportOriginalSample> samples,
        string roleClass,
        string roleLabel)
    {
        if (samples.Count == 0)
        {
            return;
        }

        sb.AppendLine($"<div class=\"gallery-block\">");
        sb.AppendLine($"<div class=\"gallery-head\"><h3>{H(heading)}</h3><span class=\"role-pill {H(roleClass)}\">{H(roleLabel)}</span></div>");
        sb.AppendLine("<div class=\"gallery\">");
        foreach (var sample in samples)
        {
            var dataUri = TryToDataUri(sample.ImagePath);
            if (dataUri is null)
            {
                continue;
            }

            sb.AppendLine($"<figure class=\"shot {H(roleClass)}\">");
            sb.AppendLine($"<div class=\"shot-media\"><img src=\"{dataUri}\" alt=\"{H(sample.Title)}\"/></div>");
            sb.AppendLine("<figcaption>");
            sb.AppendLine($"<strong>{H(sample.Title)}</strong>");
            sb.AppendLine($"<span>{H(sample.Verdict)}</span>");
            sb.AppendLine("</figcaption>");
            sb.AppendLine("</figure>");
        }

        sb.AppendLine("</div></div>");
    }

    private static string? TryToDataUri(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return null;
            }

            var bytes = File.ReadAllBytes(path);
            var mime = Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".webp" => "image/webp",
                ".gif" => "image/gif",
                ".bmp" => "image/bmp",
                _ => "image/png",
            };
            return $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
        }
        catch
        {
            return null;
        }
    }

    private static string MetaChip(string label, string value) =>
        $"<span class=\"chip\"><em>{H(label)}</em><strong>{H(value)}</strong></span>";

    private static string MetricCard(string label, string value, string meta, string tone) =>
        $"<article class=\"metric tone-{H(tone)}\">"
        + $"<span class=\"metric-label\">{H(label)}</span>"
        + $"<strong class=\"metric-value\">{H(value)}</strong>"
        + (string.IsNullOrWhiteSpace(meta) ? string.Empty : $"<span class=\"metric-meta\">{H(meta)}</span>")
        + "</article>";

    private static string ConfusionCell(string key, int value, string caption, string tone) =>
        $"<div class=\"c-cell {H(tone)}\">"
        + $"<span class=\"c-key\">{H(key)}</span>"
        + $"<strong class=\"c-val\">{value.ToString(CultureInfo.InvariantCulture)}</strong>"
        + $"<span class=\"c-cap\">{H(caption)}</span>"
        + "</div>";

    private static string H(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    private const string Css =
        """
        :root {
          color-scheme: light;
          --bg: #eef1f6;
          --page: #ffffff;
          --text: #111827;
          --muted: #6b7280;
          --subtle: #9ca3af;
          --line: #e5e7eb;
          --line-soft: #f1f3f7;
          --shadow: 0 1px 2px rgba(16,24,40,.04), 0 8px 24px rgba(16,24,40,.06);
          --radius: 14px;
          --radius-sm: 10px;
          --ok: #16a34a;
          --ok-bg: #ecfdf3;
          --ok-bd: #bbf7d0;
          --ng: #dc2626;
          --ng-bg: #fef2f2;
          --ng-bd: #fecaca;
          --miss: #d97706;
          --miss-bg: #fffbeb;
          --miss-bd: #fde68a;
          --fp: #7c3aed;
          --fp-bg: #f5f3ff;
          --fp-bd: #ddd6fe;
          --accent: #2563eb;
          --accent-bg: #eff6ff;
          --accent-bd: #bfdbfe;
        }

        * { box-sizing: border-box; }
        html { -webkit-text-size-adjust: 100%; }
        body {
          margin: 0;
          min-height: 100vh;
          font: 14px/1.55 "Segoe UI", "Microsoft YaHei UI", "PingFang SC", system-ui, sans-serif;
          color: var(--text);
          background:
            radial-gradient(1200px 480px at 10% -10%, #dbeafe 0%, transparent 55%),
            radial-gradient(900px 420px at 100% 0%, #fce7f3 0%, transparent 50%),
            var(--bg);
        }

        .page {
          width: min(1080px, calc(100% - 32px));
          margin: 24px auto 40px;
          background: var(--page);
          border: 1px solid rgba(229,231,235,.9);
          border-radius: 20px;
          box-shadow: var(--shadow);
          overflow: hidden;
        }

        .hero {
          padding: 28px 32px 22px;
          border-bottom: 1px solid var(--line);
          background: linear-gradient(180deg, #fbfdff 0%, #ffffff 100%);
        }
        .hero-top {
          display: flex;
          align-items: center;
          gap: 10px;
          margin-bottom: 12px;
        }
        .brand {
          font-size: 12px;
          font-weight: 700;
          letter-spacing: .04em;
          text-transform: uppercase;
          color: var(--accent);
        }
        .badge {
          display: inline-flex;
          align-items: center;
          padding: 2px 10px;
          border-radius: 999px;
          font-size: 12px;
          color: #1e40af;
          background: var(--accent-bg);
          border: 1px solid var(--accent-bd);
        }
        h1 {
          margin: 0 0 14px;
          font-size: 24px;
          line-height: 1.25;
          font-weight: 700;
          letter-spacing: -0.02em;
        }
        .meta-row {
          display: flex;
          flex-wrap: wrap;
          gap: 8px;
        }
        .chip {
          display: inline-flex;
          align-items: baseline;
          gap: 8px;
          max-width: 100%;
          padding: 6px 12px;
          border-radius: 999px;
          background: #f8fafc;
          border: 1px solid var(--line);
          font-size: 12px;
        }
        .chip em {
          font-style: normal;
          color: var(--muted);
          flex-shrink: 0;
        }
        .chip strong {
          font-weight: 600;
          color: var(--text);
          overflow: hidden;
          text-overflow: ellipsis;
          white-space: nowrap;
          max-width: 420px;
        }

        .panel {
          padding: 22px 32px;
          border-bottom: 1px solid var(--line-soft);
        }
        .panel:last-of-type { border-bottom: none; }
        .panel-head {
          display: flex;
          align-items: baseline;
          justify-content: space-between;
          gap: 12px;
          margin-bottom: 14px;
        }
        h2 {
          margin: 0;
          font-size: 16px;
          font-weight: 700;
          letter-spacing: -0.01em;
        }
        .panel-hint {
          color: var(--muted);
          font-size: 12px;
          text-align: right;
        }

        .metrics {
          display: grid;
          grid-template-columns: repeat(4, minmax(0, 1fr));
          gap: 12px;
        }
        .metric {
          position: relative;
          padding: 14px 14px 12px;
          border-radius: var(--radius-sm);
          border: 1px solid var(--line);
          background: #fafbfc;
          overflow: hidden;
        }
        .metric::before {
          content: "";
          position: absolute;
          left: 0; top: 0; bottom: 0;
          width: 3px;
          background: #cbd5e1;
        }
        .metric.tone-accent::before { background: var(--accent); }
        .metric.tone-ok::before { background: var(--ok); }
        .metric.tone-danger::before { background: var(--ng); }
        .metric.tone-warn::before { background: var(--miss); }
        .metric.tone-violet::before { background: var(--fp); }
        .metric.tone-accent { background: linear-gradient(180deg, #f8fbff, #fafbfc); }
        .metric.tone-ok { background: linear-gradient(180deg, #f7fdf9, #fafbfc); }
        .metric.tone-danger { background: linear-gradient(180deg, #fff8f8, #fafbfc); }
        .metric.tone-warn { background: linear-gradient(180deg, #fffbf3, #fafbfc); }
        .metric.tone-violet { background: linear-gradient(180deg, #faf8ff, #fafbfc); }
        .metric-label {
          display: block;
          font-size: 12px;
          color: var(--muted);
          margin-bottom: 6px;
        }
        .metric-value {
          display: block;
          font-size: 20px;
          font-weight: 700;
          letter-spacing: -0.02em;
          line-height: 1.2;
          word-break: break-word;
        }
        .metric-meta {
          display: block;
          margin-top: 6px;
          font-size: 11px;
          color: var(--subtle);
        }

        .confusion {
          margin-top: 16px;
          padding: 14px;
          border: 1px solid var(--line);
          border-radius: var(--radius-sm);
          background: #fcfcfd;
        }
        .confusion-title {
          font-size: 12px;
          font-weight: 600;
          color: var(--muted);
          margin-bottom: 10px;
        }
        .confusion-grid {
          display: grid;
          grid-template-columns: repeat(4, minmax(0, 1fr));
          gap: 10px;
        }
        .c-cell {
          padding: 12px;
          border-radius: 10px;
          border: 1px solid var(--line);
          background: #fff;
          text-align: center;
        }
        .c-cell.tp { background: var(--ok-bg); border-color: var(--ok-bd); }
        .c-cell.tn { background: #f8fafc; border-color: var(--line); }
        .c-cell.fp { background: var(--fp-bg); border-color: var(--fp-bd); }
        .c-cell.fn { background: var(--miss-bg); border-color: var(--miss-bd); }
        .c-key {
          display: block;
          font-size: 11px;
          font-weight: 700;
          letter-spacing: .04em;
          color: var(--muted);
        }
        .c-val {
          display: block;
          margin: 4px 0 2px;
          font-size: 22px;
          font-weight: 700;
          font-variant-numeric: tabular-nums;
        }
        .c-cap { display: block; font-size: 11px; color: var(--subtle); }
        .confusion-note {
          margin: 10px 0 0;
          font-size: 12px;
          color: var(--muted);
          font-variant-numeric: tabular-nums;
        }

        .gallery-block { margin-top: 6px; }
        .gallery-block + .gallery-block { margin-top: 18px; }
        .gallery-head {
          display: flex;
          align-items: center;
          gap: 8px;
          margin: 0 0 10px;
        }
        h3 {
          margin: 0;
          font-size: 13px;
          font-weight: 600;
          color: var(--text);
        }
        .role-pill {
          display: inline-flex;
          align-items: center;
          padding: 1px 8px;
          border-radius: 999px;
          font-size: 11px;
          font-weight: 600;
          border: 1px solid var(--line);
          background: #fff;
          color: var(--muted);
        }
        .role-pill.ok { color: var(--ok); background: var(--ok-bg); border-color: var(--ok-bd); }
        .role-pill.ng { color: var(--ng); background: var(--ng-bg); border-color: var(--ng-bd); }
        .role-pill.miss { color: var(--miss); background: var(--miss-bg); border-color: var(--miss-bd); }
        .role-pill.false_alarm { color: var(--fp); background: var(--fp-bg); border-color: var(--fp-bd); }

        .gallery {
          display: grid;
          grid-template-columns: repeat(4, minmax(0, 1fr));
          gap: 12px;
        }
        .shot {
          margin: 0;
          border-radius: 12px;
          border: 1px solid var(--line);
          background: #fff;
          overflow: hidden;
          box-shadow: 0 1px 2px rgba(16,24,40,.03);
        }
        .shot.ok { border-color: var(--ok-bd); }
        .shot.ng { border-color: var(--ng-bd); }
        .shot.miss { border-color: var(--miss-bd); }
        .shot.false_alarm { border-color: var(--fp-bd); }
        .shot-media {
          background: linear-gradient(180deg, #111827, #1f2937);
          aspect-ratio: 4 / 3;
          display: flex;
          align-items: center;
          justify-content: center;
        }
        .shot img {
          display: block;
          width: 100%;
          height: 100%;
          object-fit: contain;
        }
        .shot figcaption {
          padding: 10px 12px 12px;
          display: flex;
          flex-direction: column;
          gap: 3px;
          border-top: 1px solid var(--line-soft);
        }
        .shot figcaption strong {
          font-size: 12px;
          font-weight: 650;
        }
        .shot figcaption span {
          font-size: 11px;
          color: var(--muted);
          line-height: 1.4;
        }

        .empty {
          display: flex;
          flex-direction: column;
          align-items: center;
          justify-content: center;
          gap: 8px;
          padding: 28px 16px;
          border: 1px dashed var(--line);
          border-radius: var(--radius-sm);
          background: #fafbfc;
          color: var(--muted);
        }
        .empty-icon {
          width: 36px;
          height: 36px;
          border-radius: 999px;
          display: grid;
          place-items: center;
          background: var(--ok-bg);
          color: var(--ok);
          border: 1px solid var(--ok-bd);
          font-weight: 700;
        }
        .empty p { margin: 0; font-size: 13px; }

        .table-wrap {
          border: 1px solid var(--line);
          border-radius: var(--radius-sm);
          overflow: auto;
          background: #fff;
        }
        table {
          width: 100%;
          border-collapse: collapse;
          font-size: 13px;
          min-width: 640px;
        }
        thead th {
          position: sticky;
          top: 0;
          z-index: 1;
          background: #f8fafc;
          color: var(--muted);
          font-weight: 600;
          font-size: 12px;
          text-align: left;
          padding: 10px 12px;
          border-bottom: 1px solid var(--line);
          white-space: nowrap;
        }
        tbody td {
          padding: 11px 12px;
          border-bottom: 1px solid var(--line-soft);
          vertical-align: middle;
        }
        tbody tr:last-child td { border-bottom: none; }
        tbody tr:hover td { background: #f9fafb; }
        .cell-name {
          font-weight: 600;
          max-width: 280px;
          overflow: hidden;
          text-overflow: ellipsis;
          white-space: nowrap;
        }
        .cell-muted { color: var(--muted); font-size: 12px; }
        .col-num {
          text-align: right;
          font-variant-numeric: tabular-nums;
          white-space: nowrap;
        }
        .tag {
          display: inline-flex;
          align-items: center;
          padding: 2px 8px;
          border-radius: 999px;
          font-size: 11px;
          font-weight: 650;
          border: 1px solid transparent;
        }
        .tag-miss { color: #92400e; background: var(--miss-bg); border-color: var(--miss-bd); }
        .tag-fp { color: #5b21b6; background: var(--fp-bg); border-color: var(--fp-bd); }
        .tag-neutral { color: var(--muted); background: #f3f4f6; border-color: var(--line); }

        .foot {
          display: flex;
          justify-content: space-between;
          gap: 12px;
          flex-wrap: wrap;
          padding: 14px 32px 18px;
          color: var(--subtle);
          font-size: 11px;
          background: #fafbfc;
          border-top: 1px solid var(--line);
        }

        @media (max-width: 900px) {
          .metrics, .metrics-eval, .confusion-grid { grid-template-columns: repeat(2, minmax(0, 1fr)); }
          .gallery { grid-template-columns: repeat(2, minmax(0, 1fr)); }
          .chip strong { max-width: 240px; }
        }
        @media (max-width: 560px) {
          .page { width: calc(100% - 16px); margin: 12px auto 24px; border-radius: 14px; }
          .hero, .panel { padding-left: 16px; padding-right: 16px; }
          .foot { padding-left: 16px; padding-right: 16px; }
          h1 { font-size: 20px; }
          .metrics, .metrics-eval, .confusion-grid, .gallery { grid-template-columns: 1fr; }
          .panel-head { flex-direction: column; align-items: flex-start; }
          .panel-hint { text-align: left; }
        }

        @page { size: A4; margin: 12mm; }
        @media print {
          body { background: #fff; }
          .page {
            width: 100%;
            margin: 0;
            border: none;
            border-radius: 0;
            box-shadow: none;
          }
          .hero { background: #fff; }
          .metric, .shot, .c-cell, tr { break-inside: avoid; }
          tbody tr:hover td { background: transparent; }
          .table-wrap { overflow: visible; }
          table { min-width: 0; }
        }
        """;
}
