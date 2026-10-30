using CommunityToolkit.Mvvm.ComponentModel;

using DeepVisionSharp.Application.Models;
using DeepVisionSharp.Application.Training;

namespace DeepVisionSharp.Shell.ViewModels.Training;

/// <summary>
/// Network input size form state for classification.
/// Owned by <see cref="TrainingConfigViewModel"/>; model type is pushed via <see cref="SetModelType"/>.
/// </summary>
public sealed partial class TrainingSpatialSettingsViewModel : ObservableObject
{
    private string _modelType = ModelTypeNames.Default;
    private bool _suppressInputSizeUserEditMark;
    private bool _inputSizeUserEdited;

    [ObservableProperty]
    private int _networkInputWidth = 256;

    [ObservableProperty]
    private int _networkInputHeight = 256;

    /// <summary>Legacy square edge: get max(W,H); set both W and H.</summary>
    public int NetworkInputSize
    {
        get => Math.Max(NetworkInputWidth, NetworkInputHeight);
        set
        {
            NetworkInputWidth = value;
            NetworkInputHeight = value;
        }
    }

    public bool InputSizeUserEdited => _inputSizeUserEdited;

    /// <summary>
    /// When true, input-size changes do not mark the field as user-edited
    /// (mirrors parent draft-save suppress during ApplyFormFromJob / Reset).
    /// </summary>
    public bool SuppressUserEditTracking { get; set; }

    public NetworkInputSizePolicy? ActiveNetworkInputSizePolicy =>
        NetworkInputSizePolicy.ForModelType(_modelType);

    public bool UsesNetworkInputSize => ActiveNetworkInputSizePolicy is not null;

    public int NetworkInputSizeMin => ActiveNetworkInputSizePolicy?.Min ?? 64;

    public int NetworkInputSizeMax => ActiveNetworkInputSizePolicy?.Max ?? 2048;

    public int NetworkInputSizeStep => ActiveNetworkInputSizePolicy?.Step ?? 32;

    public string NetworkInputSizeHint
    {
        get
        {
            var policy = ActiveNetworkInputSizePolicy;
            if (policy is null)
            {
                return "当前模型不使用网络输入尺寸。";
            }

            return $"网络输入宽×高，训练和推理固定使用该尺寸。宽高各自范围 {policy.Min}–{policy.Max}，须为 {policy.Step} 的倍数。";
        }
    }

    /// <summary>
    /// Push current model type so input-size policy recomputes.
    /// When the family changes, re-normalizes the current size to the new range.
    /// </summary>
    public void SetModelType(string modelType)
    {
        _modelType = string.IsNullOrWhiteSpace(modelType) ? string.Empty : modelType.Trim();
        NotifyPolicyDerivedState();
        if (UsesNetworkInputSize)
        {
            NetworkInputWidth = NormalizeNetworkInputSize(NetworkInputWidth);
            NetworkInputHeight = NormalizeNetworkInputSize(NetworkInputHeight);
        }
    }

    public void NotifyProjectWorkflowChanged() => NotifyPolicyDerivedState();

    public void NotifyPolicyDerivedState()
    {
        OnPropertyChanged(nameof(ActiveNetworkInputSizePolicy));
        OnPropertyChanged(nameof(UsesNetworkInputSize));
        OnPropertyChanged(nameof(NetworkInputSizeMin));
        OnPropertyChanged(nameof(NetworkInputSizeMax));
        OnPropertyChanged(nameof(NetworkInputSizeStep));
        OnPropertyChanged(nameof(NetworkInputSizeHint));
        OnPropertyChanged(nameof(NetworkInputSize));
    }

    public int NormalizeNetworkInputSize(int raw)
    {
        var policy = ActiveNetworkInputSizePolicy;
        if (policy is null)
        {
            return raw;
        }

        return policy.Normalize(raw);
    }

    public int DefaultNetworkInputSize()
        => ActiveNetworkInputSizePolicy?.Default ?? 512;

    public void ResetToDefaults()
    {
        _inputSizeUserEdited = false;
        var def = DefaultNetworkInputSize();
        NetworkInputWidth = def;
        NetworkInputHeight = def;
    }

    public void ApplyToForm(TrainingJobForm form)
    {
        form.NetworkInputWidth = NormalizeNetworkInputSize(NetworkInputWidth);
        form.NetworkInputHeight = NormalizeNetworkInputSize(NetworkInputHeight);
    }

    public void SetNetworkInputSizeFromUser(int inputSize)
    {
        _inputSizeUserEdited = true;
        var normalized = NormalizeNetworkInputSize(inputSize);
        NetworkInputWidth = normalized;
        NetworkInputHeight = normalized;
    }

    public void SetNetworkInputWidthFromUser(int width)
    {
        _inputSizeUserEdited = true;
        NetworkInputWidth = NormalizeNetworkInputSize(width);
    }

    public void SetNetworkInputHeightFromUser(int height)
    {
        _inputSizeUserEdited = true;
        NetworkInputHeight = NormalizeNetworkInputSize(height);
    }

    /// <summary>Seed the product default when the user has not edited the input size.</summary>
    public void TryApplySuggestedDefault(bool isFormEditable, bool suppressDraftSave)
    {
        if (suppressDraftSave
            || !isFormEditable
            || !UsesNetworkInputSize
            || _inputSizeUserEdited)
        {
            return;
        }

        var normalized = NormalizeNetworkInputSize(DefaultNetworkInputSize());
        if (NetworkInputWidth == normalized && NetworkInputHeight == normalized)
        {
            return;
        }

        _suppressInputSizeUserEditMark = true;
        try
        {
            NetworkInputWidth = normalized;
            NetworkInputHeight = normalized;
        }
        finally
        {
            _suppressInputSizeUserEditMark = false;
        }
    }

    partial void OnNetworkInputWidthChanged(int value) => OnNetworkInputDimensionChanged(value, isWidth: true);

    partial void OnNetworkInputHeightChanged(int value) => OnNetworkInputDimensionChanged(value, isWidth: false);

    private void OnNetworkInputDimensionChanged(int value, bool isWidth)
    {
        if (_suppressInputSizeUserEditMark)
        {
            NotifyInputSizeBindings();
            return;
        }

        if (!SuppressUserEditTracking)
        {
            var normalized = NormalizeNetworkInputSize(value);
            if (normalized != value)
            {
                _suppressInputSizeUserEditMark = true;
                try
                {
                    _inputSizeUserEdited = true;
                    if (isWidth)
                    {
                        NetworkInputWidth = normalized;
                    }
                    else
                    {
                        NetworkInputHeight = normalized;
                    }
                }
                finally
                {
                    _suppressInputSizeUserEditMark = false;
                }

                return;
            }

            _inputSizeUserEdited = true;
        }

        NotifyInputSizeBindings();
    }

    private void NotifyInputSizeBindings()
    {
        OnPropertyChanged(nameof(NetworkInputSize));
        OnPropertyChanged(nameof(NetworkInputSizeHint));
    }
}
