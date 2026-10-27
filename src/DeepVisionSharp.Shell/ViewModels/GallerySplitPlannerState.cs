using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using DeepVisionSharp.Application.Galleries;

namespace DeepVisionSharp.Shell.ViewModels;

public sealed partial class GallerySplitPlannerState : ObservableObject
{
    public const string ModeManual = "Manual";
    public const string ModeByRatio = "ByRatio";
    public const string ModeByCount = "ByCount";
    public const string ModeAnnotated = "Annotated";
    public const string ModeUnannotated = "Unannotated";
    public const string ModeFileNamePrefix = "FileNamePrefix";
    public const string ModeWithError = "WithError";
    public const string ModeByLabel = "ByLabel";
    public const string ModeByLabelRatio = "ByLabelRatio";

    [ObservableProperty]
    private string _galleryName = string.Empty;

    [ObservableProperty]
    private int _totalSampleCount;

    [ObservableProperty]
    private string _splitMode = ModeByRatio;

    [ObservableProperty]
    private double _moveRatio = 0.8;

    [ObservableProperty]
    private int _moveRatioPercent = 80;

    [ObservableProperty]
    private int _moveCount = 1;

    [ObservableProperty]
    private bool _preserveFileOrder;

    [ObservableProperty]
    private string _randomSeedText = string.Empty;

    [ObservableProperty]
    private string _fileNamePrefixText = string.Empty;

    [ObservableProperty]
    private string _selectedLabel = string.Empty;

    [ObservableProperty]
    private string _previewText = string.Empty;

    [ObservableProperty]
    private int _previewMoveSampleCount;

    [ObservableProperty]
    private int _previewRemainingSampleCount;

    [ObservableProperty]
    private bool _isPreviewLoading;

    [ObservableProperty]
    private string _previewErrorMessage = string.Empty;

    [ObservableProperty]
    private string _dialogErrorMessage = string.Empty;

    public bool IsManualMode => SplitMode == ModeManual;

    public bool IsByRatioMode => SplitMode == ModeByRatio;

    public bool IsByCountMode => SplitMode == ModeByCount;

    public bool IsAnnotatedMode => SplitMode == ModeAnnotated;

    public bool IsUnannotatedMode => SplitMode == ModeUnannotated;

    public bool IsFileNamePrefixMode => SplitMode == ModeFileNamePrefix;

    public bool IsWithErrorMode => SplitMode == ModeWithError;

    public bool IsByLabelMode => SplitMode == ModeByLabel;

    public bool IsByLabelRatioMode => SplitMode == ModeByLabelRatio;

    public bool ShowRatioOptions => IsByRatioMode || IsByLabelRatioMode;

    public bool ShowCountOptions => IsByCountMode;

    public bool ShowFileNamePrefixOptions => IsFileNamePrefixMode;

    public bool ShowLabelOptions => IsByLabelMode || IsByLabelRatioMode;

    public bool ShowManualOptions => IsManualMode;

    public bool IsRandomSampling => !PreserveFileOrder;

    /// <summary>源图库至少保留 1 张时的最大可移动数量。</summary>
    public int MaxMoveCount => Math.Max(1, TotalSampleCount - 1);

    public bool HasPreviewMetrics => !IsManualMode && HasPreview && !HasPreviewError;

    public bool HasPreviewError => !string.IsNullOrWhiteSpace(PreviewErrorMessage);

    public bool IsRandomSeedValid =>
        PreserveFileOrder
        || string.IsNullOrWhiteSpace(RandomSeedText)
        || int.TryParse(RandomSeedText.Trim(), out _);

    public bool HasRandomSeedError => !IsRandomSeedValid;

    public bool CanContinue =>
        IsManualMode
        || (!IsPreviewLoading
            && HasPreview
            && !HasPreviewError
            && IsRandomSeedValid
            && PreviewMoveSampleCount > 0
            && PreviewRemainingSampleCount > 0);

    public string StepText => IsManualMode ? "步骤 1 / 3 · 选择规则" : "步骤 1 / 2 · 选择规则";

    public string SelectedModeTitle => SplitMode switch
    {
        ModeManual => "手动选择样本",
        ModeByRatio => "按比例拆分",
        ModeByCount => "按数量拆分",
        ModeAnnotated => "移动全部已标注样本",
        ModeUnannotated => "移动全部未标注样本",
        ModeFileNamePrefix => "按文件名前缀拆分",
        ModeWithError => "移动全部错误样本",
        ModeByLabel => "按缺陷类别拆分",
        ModeByLabelRatio => "按类别比例拆分",
        _ => "拆分规则"
    };

    public string SelectedModeDescription => SplitMode switch
    {
        ModeManual => "下一步将打开样本选择器，可分页勾选需要移动的样本。",
        ModeByRatio => "从当前图库中按指定比例抽取样本。",
        ModeByCount => "从当前图库中按指定张数抽取样本。",
        ModeAnnotated => "移动所有已经完成标注的样本。",
        ModeUnannotated => "移动所有尚未标注的样本。",
        ModeFileNamePrefix => "移动文件名以指定文本开头的样本。",
        ModeWithError => "移动当前被标记为错误状态的样本。",
        ModeByLabel => "移动包含指定缺陷类别标注的全部样本。",
        ModeByLabelRatio => "从指定缺陷类别的样本中按比例抽取。",
        _ => string.Empty
    };

    public bool HasAvailableLabels => AvailableLabels.Count > 0;

    public ObservableCollection<string> AvailableLabels { get; } = new();

    public bool HasDialogErrorMessage => !string.IsNullOrWhiteSpace(DialogErrorMessage);

    public bool HasPreview => !string.IsNullOrWhiteSpace(PreviewText);

    public void Reset(string galleryName, int totalSampleCount)
    {
        GalleryName = galleryName;
        TotalSampleCount = totalSampleCount;
        SplitMode = ModeByRatio;
        MoveRatio = 0.8;
        MoveRatioPercent = 80;
        MoveCount = DefaultMoveCount(totalSampleCount);
        PreserveFileOrder = false;
        RandomSeedText = string.Empty;
        FileNamePrefixText = string.Empty;
        SelectedLabel = string.Empty;
        AvailableLabels.Clear();
        OnPropertyChanged(nameof(HasAvailableLabels));
        OnPropertyChanged(nameof(MaxMoveCount));
        ClearPreview();
        ClearDialogError();
    }

    public GallerySplitMode ToSplitMode()
        => SplitMode switch
        {
            ModeManual => GallerySplitMode.Manual,
            ModeByRatio => GallerySplitMode.ByRatio,
            ModeByCount => GallerySplitMode.ByCount,
            ModeAnnotated => GallerySplitMode.Annotated,
            ModeUnannotated => GallerySplitMode.Unannotated,
            ModeFileNamePrefix => GallerySplitMode.FileNamePrefix,
            ModeWithError => GallerySplitMode.WithError,
            ModeByLabel => GallerySplitMode.ByLabel,
            ModeByLabelRatio => GallerySplitMode.ByLabelRatio,
            _ => GallerySplitMode.ByRatio
        };

    private static int DefaultMoveCount(int totalSampleCount)
    {
        if (totalSampleCount < 2)
        {
            return 1;
        }

        return Math.Clamp(totalSampleCount / 2, 1, totalSampleCount - 1);
    }

    public void SetAvailableLabels(IReadOnlyList<string> labels)
    {
        AvailableLabels.Clear();
        foreach (var label in labels.OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
        {
            AvailableLabels.Add(label);
        }

        SelectedLabel = AvailableLabels.FirstOrDefault() ?? string.Empty;
        OnPropertyChanged(nameof(HasAvailableLabels));
    }

    public string? GetFileNamePrefix()
    {
        if (SplitMode != ModeFileNamePrefix)
        {
            return null;
        }

        var prefix = FileNamePrefixText.Trim();
        return string.IsNullOrWhiteSpace(prefix) ? null : prefix;
    }

    public string? GetTargetLabel()
    {
        if (SplitMode is not ModeByLabel and not ModeByLabelRatio)
        {
            return null;
        }

        var label = SelectedLabel.Trim();
        return string.IsNullOrWhiteSpace(label) ? null : label;
    }

    public int? ParseRandomSeed()
    {
        if (string.IsNullOrWhiteSpace(RandomSeedText))
        {
            return null;
        }

        return int.TryParse(RandomSeedText.Trim(), out var seed) ? seed : null;
    }

    public void SetDialogError(string message)
    {
        DialogErrorMessage = message;
        OnPropertyChanged(nameof(HasDialogErrorMessage));
    }

    public void ClearDialogError()
    {
        DialogErrorMessage = string.Empty;
        OnPropertyChanged(nameof(HasDialogErrorMessage));
    }

    public void BeginPreview()
    {
        IsPreviewLoading = true;
        PreviewText = string.Empty;
        PreviewErrorMessage = string.Empty;
        PreviewMoveSampleCount = 0;
        PreviewRemainingSampleCount = 0;
        NotifyPreviewStateChanged();
    }

    public void SetPreview(int moveSampleCount, int totalSampleCount, string previewText)
    {
        IsPreviewLoading = false;
        PreviewMoveSampleCount = moveSampleCount;
        PreviewRemainingSampleCount = Math.Max(0, totalSampleCount - moveSampleCount);
        PreviewText = previewText;
        PreviewErrorMessage = string.Empty;
        NotifyPreviewStateChanged();
    }

    public void SetManualPreview()
    {
        IsPreviewLoading = false;
        PreviewMoveSampleCount = 0;
        PreviewRemainingSampleCount = TotalSampleCount;
        PreviewText = "下一步将打开样本选择器，可分页勾选需要移动的样本。";
        PreviewErrorMessage = string.Empty;
        NotifyPreviewStateChanged();
    }

    public void SetPreviewError(string message)
    {
        IsPreviewLoading = false;
        PreviewMoveSampleCount = 0;
        PreviewRemainingSampleCount = TotalSampleCount;
        PreviewText = string.Empty;
        PreviewErrorMessage = message;
        NotifyPreviewStateChanged();
    }

    private void ClearPreview()
    {
        IsPreviewLoading = false;
        PreviewMoveSampleCount = 0;
        PreviewRemainingSampleCount = TotalSampleCount;
        PreviewText = string.Empty;
        PreviewErrorMessage = string.Empty;
        NotifyPreviewStateChanged();
    }

    private void NotifyPreviewStateChanged()
    {
        OnPropertyChanged(nameof(HasPreview));
        OnPropertyChanged(nameof(HasPreviewMetrics));
        OnPropertyChanged(nameof(HasPreviewError));
        OnPropertyChanged(nameof(CanContinue));
    }

    [RelayCommand]
    private void SelectManualMode() => SplitMode = ModeManual;

    [RelayCommand]
    private void SelectByRatioMode() => SplitMode = ModeByRatio;

    [RelayCommand]
    private void SelectByCountMode() => SplitMode = ModeByCount;

    [RelayCommand]
    private void SelectAnnotatedMode() => SplitMode = ModeAnnotated;

    [RelayCommand]
    private void SelectUnannotatedMode() => SplitMode = ModeUnannotated;

    [RelayCommand]
    private void SelectFileNamePrefixMode() => SplitMode = ModeFileNamePrefix;

    [RelayCommand]
    private void SelectWithErrorMode() => SplitMode = ModeWithError;

    [RelayCommand]
    private void SelectByLabelMode() => SplitMode = ModeByLabel;

    [RelayCommand]
    private void SelectByLabelRatioMode() => SplitMode = ModeByLabelRatio;

    [RelayCommand]
    private void SelectRandomSampling() => PreserveFileOrder = false;

    [RelayCommand]
    private void SelectFileOrderSampling() => PreserveFileOrder = true;

    partial void OnSplitModeChanged(string value)
    {
        OnPropertyChanged(nameof(IsManualMode));
        OnPropertyChanged(nameof(IsByRatioMode));
        OnPropertyChanged(nameof(IsByCountMode));
        OnPropertyChanged(nameof(IsAnnotatedMode));
        OnPropertyChanged(nameof(IsUnannotatedMode));
        OnPropertyChanged(nameof(IsFileNamePrefixMode));
        OnPropertyChanged(nameof(IsWithErrorMode));
        OnPropertyChanged(nameof(IsByLabelMode));
        OnPropertyChanged(nameof(IsByLabelRatioMode));
        OnPropertyChanged(nameof(ShowRatioOptions));
        OnPropertyChanged(nameof(ShowCountOptions));
        OnPropertyChanged(nameof(ShowFileNamePrefixOptions));
        OnPropertyChanged(nameof(ShowLabelOptions));
        OnPropertyChanged(nameof(ShowManualOptions));
        OnPropertyChanged(nameof(HasPreviewMetrics));
        OnPropertyChanged(nameof(StepText));
        OnPropertyChanged(nameof(SelectedModeTitle));
        OnPropertyChanged(nameof(SelectedModeDescription));
        OnPropertyChanged(nameof(CanContinue));
    }

    partial void OnMoveRatioPercentChanged(int value)
    {
        MoveRatio = value / 100d;
    }

    partial void OnTotalSampleCountChanged(int value)
    {
        OnPropertyChanged(nameof(MaxMoveCount));
        if (MoveCount > MaxMoveCount)
        {
            MoveCount = MaxMoveCount;
        }
        else if (MoveCount < 1)
        {
            MoveCount = 1;
        }
    }

    partial void OnMoveCountChanged(int value)
    {
        if (value < 1)
        {
            MoveCount = 1;
            return;
        }

        if (MaxMoveCount > 0 && value > MaxMoveCount)
        {
            MoveCount = MaxMoveCount;
        }
    }

    partial void OnPreserveFileOrderChanged(bool value)
    {
        OnPropertyChanged(nameof(IsRandomSampling));
        OnPropertyChanged(nameof(IsRandomSeedValid));
        OnPropertyChanged(nameof(HasRandomSeedError));
        OnPropertyChanged(nameof(CanContinue));
    }

    partial void OnRandomSeedTextChanged(string value)
    {
        OnPropertyChanged(nameof(IsRandomSeedValid));
        OnPropertyChanged(nameof(HasRandomSeedError));
        OnPropertyChanged(nameof(CanContinue));
    }

    partial void OnIsPreviewLoadingChanged(bool value)
        => OnPropertyChanged(nameof(CanContinue));

    partial void OnPreviewTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasPreview));
        OnPropertyChanged(nameof(HasPreviewMetrics));
        OnPropertyChanged(nameof(CanContinue));
    }

    partial void OnPreviewErrorMessageChanged(string value)
    {
        OnPropertyChanged(nameof(HasPreviewError));
        OnPropertyChanged(nameof(HasPreviewMetrics));
        OnPropertyChanged(nameof(CanContinue));
    }

    partial void OnPreviewMoveSampleCountChanged(int value)
        => OnPropertyChanged(nameof(CanContinue));

    partial void OnPreviewRemainingSampleCountChanged(int value)
        => OnPropertyChanged(nameof(CanContinue));
}
