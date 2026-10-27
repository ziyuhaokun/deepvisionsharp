using System.Diagnostics;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeepVisionSharp.Application.Galleries;
using DeepVisionSharp.Application.Inference;
using DeepVisionSharp.Shell.Services;
using DeepVisionSharp.Shell.Services.Dialogs;
using DeepVisionSharp.Shell.Services.Reporting;

namespace DeepVisionSharp.Shell.ViewModels.Inference;

public sealed partial class InferenceReportViewModel : ObservableObject
{
    private const int ThumbnailDecodeWidth = 240;

    private readonly IAppDialogService _appDialogService;
    private readonly IClipboardService _clipboard;
    private readonly ILocalFileDialogService _fileDialog;
    private readonly IImageSourceFactory _imageSources;
    private readonly ShellSnackbarNotifier _snackbar;

    public InferenceReportViewModel(
        IAppDialogService appDialogService,
        IClipboardService clipboard,
        ILocalFileDialogService fileDialog,
        IImageSourceFactory imageSources,
        ShellSnackbarNotifier snackbar)
    {
        _appDialogService = appDialogService;
        _clipboard = clipboard;
        _fileDialog = fileDialog;
        _imageSources = imageSources;
        _snackbar = snackbar;
    }

    [ObservableProperty]
    private string _resultHeadline = string.Empty;

    [ObservableProperty]
    private string _resultDetail = string.Empty;

    [ObservableProperty]
    private string _resultPerformance = string.Empty;

    [ObservableProperty]
    private bool _hasResult;

    [ObservableProperty]
    private bool _lastResultFailed;

    [ObservableProperty]
    private bool _hasEvaluationPairs;

    [ObservableProperty]
    private string _evaluationSummaryText = string.Empty;

    [ObservableProperty]
    private InferenceBatchReport? _lastBatchReport;

    public void RefreshEvaluationSummary(IEnumerable<InferenceImageItem> items)
    {
        var eval = InferenceOkNgEvaluation.FromItems(items);
        HasEvaluationPairs = eval.ClassifiedCount > 0;
        EvaluationSummaryText = eval.SummaryText;
    }

    public InferenceBatchReport BuildBatchReport(
        InferencePageModelOption model,
        IReadOnlyList<InferenceImageItem> targets,
        int successCount,
        int failedCount,
        double wallClockMs,
        string? deviceText = null)
    {
        var successTimes = targets
            .Where(t => t.LastInferElapsedMs is > 0 && string.IsNullOrWhiteSpace(t.LastInferError))
            .Select(t => t.LastInferElapsedMs!.Value)
            .ToList();
        var avgMs = successTimes.Count > 0 ? successTimes.Average() : 0;
        var minMs = successTimes.Count > 0 ? successTimes.Min() : 0;
        var maxMs = successTimes.Count > 0 ? successTimes.Max() : 0;

        var samples = targets.Select(t =>
        {
            var isSuccess = t.LastInferElapsedMs is > 0 && string.IsNullOrWhiteSpace(t.LastInferError);
            return new InferenceBatchReportSampleRow
            {
                DisplayName = t.DisplayName,
                SourceLabel = t.SourceLabel,
                ExpectedText = string.IsNullOrWhiteSpace(t.ExpectedClass) ? "—" : t.ExpectedClass!,
                PredictedText = string.IsNullOrWhiteSpace(t.PredictedClass) ? "—" : t.PredictedClass!,
                EvaluationTag = string.IsNullOrWhiteSpace(t.EvaluationTag) ? "—" : t.EvaluationTag,
                ElapsedText = t.LastInferElapsedMs is > 0 ? $"{t.LastInferElapsedMs:F1} ms" : "—",
                StatusText = !string.IsNullOrWhiteSpace(t.LastInferError)
                    ? t.LastInferError!
                    : t.LastInferElapsedMs is > 0
                        ? "成功"
                        : "未运行",
                ElapsedMs = t.LastInferElapsedMs is > 0 ? t.LastInferElapsedMs : null,
                IsSuccess = isSuccess,
                ImagePath = t.ImagePath ?? string.Empty,
            };
        }).ToList();

        var eval = InferenceOkNgEvaluation.FromItems(targets);
        var originalSamples = BuildOriginalSamples(targets);

        return new InferenceBatchReport
        {
            Headline = $"批量推理分析报告 · {successCount}/{targets.Count} 成功",
            ModelText = model.DisplayText,
            DeviceText = string.IsNullOrWhiteSpace(deviceText)
                ? "默认设备"
                : deviceText!,
            GeneratedAtText = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            TotalCount = targets.Count,
            SuccessCount = successCount,
            FailedCount = failedCount,
            TotalWallMilliseconds = wallClockMs,
            AverageSuccessMilliseconds = avgMs,
            MinSuccessMilliseconds = minMs,
            MaxSuccessMilliseconds = maxMs,
            Evaluation = eval,
            Samples = samples,
            OriginalSamples = originalSamples,
        };
    }

    public async Task ShowBatchReportAsync(InferenceBatchReport report)
    {
        LastBatchReport = report;
        var dialogResult = await _appDialogService.ShowAsync(AppDialogKind.InferenceBatchReport, report)
            .ConfigureAwait(true);
        if (dialogResult == AppDialogResult.Primary)
        {
            _clipboard.SetText(report.FullReportText);
            _snackbar.ShowSuccess("分析报告已复制到剪贴板。", "推理");
            return;
        }

        if (dialogResult == AppDialogResult.Secondary)
        {
            await ExportHtmlAsync(report).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    public async Task ShowLastBatchReportAsync()
    {
        if (LastBatchReport is null)
        {
            _snackbar.ShowWarning("尚无可查看的批量推理报告，请先执行「推理全部」。", "推理");
            return;
        }

        await ShowBatchReportAsync(LastBatchReport).ConfigureAwait(true);
    }

    private async Task ExportHtmlAsync(InferenceBatchReport report)
    {
        var defaultName = $"推理报告_{DateTime.Now:yyyyMMdd_HHmmss}.html";
        var savePath = await _fileDialog.SaveFileAsync("HTML 文件|*.html;*.htm|所有文件|*.*", defaultName)
            .ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(savePath))
        {
            return;
        }

        try
        {
            var html = InferenceBatchReportHtmlBuilder.Build(report);
            await File.WriteAllTextAsync(savePath, html, Encoding.UTF8).ConfigureAwait(true);
            _snackbar.ShowSuccess("HTML 报告已导出。", "推理");
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = savePath,
                    UseShellExecute = true,
                });
            }
            catch
            {
                // Opening is best-effort; export already succeeded.
            }
        }
        catch (Exception ex)
        {
            _snackbar.ShowError($"导出 HTML 失败：{ex.Message}", "推理");
        }
    }

    private IReadOnlyList<InferenceReportOriginalSample> BuildOriginalSamples(
        IReadOnlyList<InferenceImageItem> targets)
    {
        var list = new List<InferenceReportOriginalSample>();
        AppendRole(
            list,
            targets.Where(static t =>
                    t.HasExpected && InferenceImageItem.IsOk(t.ExpectedClass)),
            role: "ok",
            titlePrefix: "OK",
            verdict: "预计 OK · 原图示例");
        AppendRole(
            list,
            targets.Where(static t =>
                    string.Equals(t.EvaluationTag, "命中", StringComparison.Ordinal)
                    || (t.HasExpected
                        && t.HasPredicted
                        && InferenceImageItem.IsNg(t.ExpectedClass)
                        && InferenceImageItem.IsNg(t.PredictedClass))),
            role: "ng",
            titlePrefix: "NG",
            verdict: "预计 NG · 检出命中原图");
        AppendRole(
            list,
            targets.Where(static t => string.Equals(t.EvaluationTag, "漏检", StringComparison.Ordinal)),
            role: "miss",
            titlePrefix: "漏检",
            verdict: "预计 NG · 推理 OK（漏检）");
        AppendRole(
            list,
            targets.Where(static t => string.Equals(t.EvaluationTag, "误报", StringComparison.Ordinal)),
            role: "false_alarm",
            titlePrefix: "误报",
            verdict: "预计 OK · 推理 NG（误报）");
        return list;
    }

    private void AppendRole(
        List<InferenceReportOriginalSample> destination,
        IEnumerable<InferenceImageItem> candidates,
        string role,
        string titlePrefix,
        string verdict)
    {
        var index = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in candidates)
        {
            if (index >= InferenceBatchReport.MaxOriginalSamplesPerRole)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(item.ImagePath)
                || !File.Exists(item.ImagePath)
                || !seen.Add(Path.GetFullPath(item.ImagePath)))
            {
                continue;
            }

            index++;
            var thumb = _imageSources.FromFileThumbnail(item.ImagePath, ThumbnailDecodeWidth);
            destination.Add(new InferenceReportOriginalSample
            {
                Role = role,
                Title = $"{titlePrefix} #{index}",
                Verdict = verdict,
                ImagePath = item.ImagePath,
                Thumbnail = thumb,
            });
        }
    }
}
