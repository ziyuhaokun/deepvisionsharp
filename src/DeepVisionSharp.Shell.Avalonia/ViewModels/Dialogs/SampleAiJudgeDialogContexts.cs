using System.Windows.Input;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DeepVisionSharp.Shell.Avalonia.ViewModels.Dialogs;

/// <summary>AI 判定批量开始对话框 stub。</summary>
public sealed class SampleAiJudgeBatchStartDialogContext
{
    public SampleAiJudgeBatchStartDialogContext()
    {
        ScopeOptions =
        [
            new SampleValueLabelItem("Unjudged", "仅未判定样本（312 张）"),
            new SampleValueLabelItem("Filtered", "当前筛选结果（184 张）"),
            new SampleValueLabelItem("All", "全部样本"),
        ];

        SelectedScope = ScopeOptions[1];
    }

    public string SummaryText => "对图库「钢板表面缺陷 · 一车间」批量请求 AI 判定，结果只写入建议，不会自动改标注。";

    public IReadOnlyList<SampleValueLabelItem> ScopeOptions { get; }

    public SampleValueLabelItem SelectedScope { get; set; }

    public string EstimateText => "预计请求 184 次，按当前并发 4 估算约 9 分钟。";

    public string StandardsText => "当前标准集：OK 6 张 / NG 9 张；判定要点 1 条。";

    public string NoteText => "判定期间可以继续标注；建议在任务中心查看进度。";
}

/// <summary>AI 判定标准集对话框 stub：两组样本卡片，缩略图缺失走「快照缺失」占位。</summary>
public sealed class SampleAiJudgeStandardsDialogContext
{
    public SampleAiJudgeStandardsDialogContext()
    {
        OkStandards =
        [
            new SampleJudgeStandard("OK-000970.png · 2025-11-10 14:02", "已标注 · OK", "OK"),
            new SampleJudgeStandard("OK-001204.png · 2025-11-10 14:03", "已标注 · OK", "OK"),
        ];

        NgStandards =
        [
            new SampleJudgeStandard("NG-000112.png · 2025-11-10 14:05", "已标注 · NG", "NG"),
            new SampleJudgeStandard("NG-000187.png · 2025-11-10 14:06", "已标注 · NG", "NG"),
            new SampleJudgeStandard("NG-000231.png · 2025-11-10 14:07", "已标注 · NG", "NG"),
        ];

        SwitchRoleCommand = new RelayCommand(() => { });
        RemoveCommand = new RelayCommand(() => { });
    }

    public string Guidance { get; set; } = "轻微色差不算缺陷；重点看边缘崩缺与压痕。";

    public string OkHeader => "OK 标准";

    public bool HasNoOkStandards => false;

    public IReadOnlyList<SampleJudgeStandard> OkStandards { get; }

    public string NgHeader => "NG 标准";

    public bool HasNoNgStandards => false;

    public IReadOnlyList<SampleJudgeStandard> NgStandards { get; }

    public string ErrorMessage => string.Empty;

    public bool HasErrorMessage => false;

    public ICommand SwitchRoleCommand { get; }

    public ICommand RemoveCommand { get; }
}

/// <summary>AI 模型设置对话框 stub。</summary>
public sealed class SampleAiJudgeModelSettingsDialogContext
{
    public SampleAiJudgeModelSettingsDialogContext()
    {
        Configurations =
        [
            new SampleDisplayNameItem("本地 Qwen2.5-VL"),
            new SampleDisplayNameItem("云端多模态（备用）"),
        ];

        SelectedConfiguration = Configurations[0];

        ModelOptions =
        [
            "Qwen/Qwen2.5-VL-7B-Instruct",
            "gpt-4o-mini",
        ];

        AddConfigurationCommand = new RelayCommand(() => { });
        LoadModelsCommand = new RelayCommand(() => { });
        TestConnectionCommand = new RelayCommand(() => { });
    }

    public bool CanEditConfigurations => true;

    public IReadOnlyList<SampleDisplayNameItem> Configurations { get; }

    public SampleDisplayNameItem SelectedConfiguration { get; set; }

    public string ConfigurationName { get; set; } = "本地 Qwen2.5-VL";

    public string BaseUrl { get; set; } = "http://127.0.0.1:8317/v1";

    public IReadOnlyList<string> ModelOptions { get; }

    public string Model { get; set; } = "Qwen/Qwen2.5-VL-7B-Instruct";

    public string ApiKey { get; set; } = string.Empty;

    public string ModelListMessage => "已从服务地址读到 2 个模型。";

    public bool ModelListFailed => false;

    public bool HasModelListMessage => true;

    public double TimeoutSeconds { get; set; } = 60;

    public double MaxConcurrency { get; set; } = 4;

    public bool UseJsonObjectFormat { get; set; } = true;

    public bool ConfirmEndpoint { get; set; }

    public string ConfirmationText =>
        "我确认该服务地址由我负责，图片与判定要点会发送到上述服务。";

    public bool ShowExternalNotice => true;

    public string ExternalNoticeText =>
        "该地址不是本机回环地址：判定时会把标准图与待判定图发到外部服务，请确认数据可以外发。";

    public bool ShowPlainHttpWarning => false;

    public bool HasTestResult => true;

    public string TestResultText =>
        "POST http://127.0.0.1:8317/v1/chat/completions → 200\nx 1.28s · 模型回复「pong」";

    public string ErrorMessage => string.Empty;

    public bool HasErrorMessage => false;

    public ICommand AddConfigurationCommand { get; }

    public ICommand LoadModelsCommand { get; }

    public ICommand TestConnectionCommand { get; }
}

/// <summary>AI 建议审阅对话框 stub：行内三列（标注 / AI 结论 / 操作）。</summary>
public sealed class SampleAiJudgeReviewDialogContext : ObservableObject
{
    private bool _isBusy;
    private string _statusText = "就绪";

    public SampleAiJudgeReviewDialogContext()
    {
        FilterOptions =
        [
            new SampleValueLabelItem("All", "全部建议"),
            new SampleValueLabelItem("Differs", "与标注不一致"),
            new SampleValueLabelItem("Stale", "已过期"),
        ];

        SelectedFilter = FilterOptions[0];

        Rows =
        [
            new SampleJudgeReviewRow("NG-000187.png", "预计 NG · 推理 OK", "2025-11-18 14:02", "OK", "NG", "ng"),
            new SampleJudgeReviewRow("OK-001204.png", "预计 OK · 推理 NG", "2025-11-18 14:03", "NG", "OK", "ok"),
            new SampleJudgeReviewRow("NG-000231.png", "预计 NG · 推理 NG", "2025-11-18 14:04", "NG", "NG", "ng"),
        ];

        CheckAllFilteredCommand = new RelayCommand(() => { });
        ClearChecksCommand = new RelayCommand(() => { });
        ConfirmAdoptCommand = new RelayCommand(() => { });
        CancelPendingCommand = new RelayCommand(() => { });
        AdoptCheckedCommand = new RelayCommand(() => { });
        AdoptFilteredCommand = new RelayCommand(() => { });
        UndoCommand = new RelayCommand(() => { });
        AdoptRowCommand = new RelayCommand(() => { });
        LocateCommand = new RelayCommand(() => { });
    }

    public string GalleryName => "钢板表面缺陷 · 一车间";

    public IReadOnlyList<SampleValueLabelItem> FilterOptions { get; }

    public SampleValueLabelItem SelectedFilter { get; set; }

    public int CheckedCount => 2;

    public IReadOnlyList<SampleJudgeReviewRow> Rows { get; }

    public bool IsEmpty => false;

    public bool HasPending => false;

    public string PendingText => "已勾选 2 张，确认后写入标注。";

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string UndoText => "撤销上次采纳";

    public ICommand CheckAllFilteredCommand { get; }

    public ICommand ClearChecksCommand { get; }

    public ICommand ConfirmAdoptCommand { get; }

    public ICommand CancelPendingCommand { get; }

    public ICommand AdoptCheckedCommand { get; }

    public ICommand AdoptFilteredCommand { get; }

    public ICommand UndoCommand { get; }

    public ICommand AdoptRowCommand { get; }

    public ICommand LocateCommand { get; }
}

/// <summary>标准集卡片 stub。</summary>
public sealed class SampleJudgeStandard
{
    public SampleJudgeStandard(string sourceText, string detailText, string switchRoleText)
    {
        SourceText = sourceText;
        DetailText = detailText;
        SwitchRoleText = switchRoleText == "OK" ? "改为 NG" : "改为 OK";
    }

    public string SourceText { get; }

    public string DetailText { get; }

    public string SwitchRoleText { get; }

    public string Note { get; set; } = string.Empty;

    public bool HasNoThumbnail => true;

    /// <summary>stub 没有快照位图，Image 的 Source 保持空。</summary>
    public Bitmap? Thumbnail => null;
}

/// <summary>审阅列表行 stub。</summary>
public sealed class SampleJudgeReviewRow : ObservableObject
{
    private bool _isChecked = true;

    public SampleJudgeReviewRow(
        string fileName,
        string metaText,
        string detailText,
        string currentText,
        string suggestionText,
        string suggestionTone)
    {
        FileName = fileName;
        MetaText = metaText;
        DetailText = detailText;
        CurrentText = currentText;
        SuggestionText = suggestionText;
        SuggestionTone = suggestionTone;
    }

    public string FileName { get; }

    public string MetaText { get; }

    public string DetailText { get; }

    public string CurrentText { get; }

    public string SuggestionText { get; }

    /// <summary>ok / ng / warn，决定 AI 结论气泡的底色。</summary>
    public string SuggestionTone { get; }

    public bool DiffersFromLabel => CurrentText != SuggestionText;

    public bool IsChecked
    {
        get => _isChecked;
        set => SetProperty(ref _isChecked, value);
    }

    /// <summary>stub 没有缩略图，Image 的 Source 保持空。</summary>
    public Bitmap? Thumbnail => null;
}
