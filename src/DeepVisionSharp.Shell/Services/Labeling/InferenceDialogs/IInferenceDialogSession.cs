using DeepVisionSharp.Application.Inference;
using DeepVisionSharp.Shell.Services.Dialogs;
using DeepVisionSharp.Shell.ViewModels.Labeling;

namespace DeepVisionSharp.Shell.Services.Labeling.InferenceDialogs;

/// <summary>
/// Per-project-workflow auto-inference dialog rules and copy.
/// Dialog XAML is exclusive per workflow; sessions must not share UI layouts.
/// </summary>
public interface IInferenceDialogSession
{
    AppDialogKind DialogKind { get; }

    string Description { get; }

    string NoModelsHint { get; }

    string ConfidenceHint { get; }

    string FooterHint { get; }

    /// <summary>Readonly policy card title (e.g. spatial strategy vs network input size).</summary>
    string PolicySectionTitle { get; }

    bool ShowsMaxDetections(InferenceModelOption? selected);

    bool ShowsBatchSize(InferenceModelOption? selected);

    /// <summary>Hint under batch control when batch is hidden (e.g. CLS fixed single image).</summary>
    string? FixedBatchHint(InferenceModelOption? selected);

    InferenceConfig BuildConfig(InferenceDialogBuildContext context);

    string FormatConfigSummary(InferenceDialogBuildContext context);

    string FormatButtonToolTip(InferenceDialogBuildContext context);
}

/// <summary>Immutable snapshot used by sessions to build config / summaries.</summary>
public sealed record InferenceDialogBuildContext(
    InferenceModelOption? SelectedModel,
    double Confidence,
    int MaxDetections,
    int BatchSize,
    string Device,
    string DeviceDisplayName);
