namespace DeepVisionSharp.Presentation.ViewModels.Models;

using DeepVisionSharp.Application.Models;
using DeepVisionSharp.Domain.Entities;
using DeepVisionSharp.Domain.ValueObjects;
using DeepVisionSharp.Presentation.Automation;

public sealed record ModelRow(
    Guid Id,
    string Name,
    string Description,
    string ModelType,
    string Status,
    IReadOnlyList<string> Classes,
    string CheckpointPath,
    DateTimeOffset CreatedAt,
    string CreatedText,
    int Epochs,
    double BestMap,
    double BestMap50,
    bool HasMetadata,
    int InputChannels)
{
    public string AutomationId => UiAutomationIds.Models.Card(Id);

    /// <summary>Card meta pill: count only (no class names — names go to edit/detail).</summary>
    public string ClassCountText => Classes.Count == 0 ? "0 类" : $"{Classes.Count} 类";

    /// <summary>卡片规格徽标。</summary>
    public string ModelTypeDisplay => ModelTypeNames.GetDisplayLabel(ModelType);

    /// <summary>Full class list for edit dialog / detail (not for card).</summary>
    public string ClassesDetailText =>
        Classes.Count == 0 ? "暂无类别" : string.Join(" · ", Classes);

    public string MetricsText =>
        HasMetadata && BestMap50 > 0
        ? IsClassificationModel
            ? $"Top-1 {BestMap50:F3}"
            : $"mAP50 {BestMap50:F3}" + (BestMap > 0 ? $" · mAP {BestMap:F3}" : string.Empty)
        : "暂无训练指标";

    public string SummaryText =>
        HasMetadata && Epochs > 0
            ? $"{InputChannelsDisplay} · {Epochs} epochs"
            : ShowsInputChannelsBadge
                ? InputChannelsDisplay
                : "尚未读取到训练元数据";

    /// <summary>Human-readable input channel mode for model cards.</summary>
    public string InputChannelsDisplay =>
        ShowsInputChannelsBadge
            ? (InputChannels == 1 ? "单通道灰度" : "RGB 三通道")
            : string.Empty;

    public bool ShowsInputChannelsBadge => IsClassificationModel;

    /// <summary>本仓库没有 OpenVINO IR 转换产物，卡片不显示 IR 徽标（deepvision ModelRow 有此字段）。</summary>
    public bool ShowsOpenVinoBadge => false;

    public string OpenVinoBadgeText => string.Empty;

    /// <summary>训练时空域参数没有写进模型元数据，卡片无法展示 pad32 / 画布尺寸徽标。</summary>
    public bool ShowsSpatialBadge => false;

    public string SpatialBadgeText => string.Empty;

    public bool CanExportPpOcrDetection => false;

    public bool CanConvertOpenVino => false;

    public bool CanQuantizeOpenVinoInt8 => false;

    public bool CanExportOpenVino => false;

    public bool IsClassificationModel =>
        ModelTypeNames.IsClassificationModelType(ModelType);
}

public sealed record ModelStatusOption(string Value, string Label);
