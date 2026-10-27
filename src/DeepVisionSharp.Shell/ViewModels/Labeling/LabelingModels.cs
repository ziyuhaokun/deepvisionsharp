using CommunityToolkit.Mvvm.ComponentModel;

using System.Windows.Media;

using DeepVisionSharp.Application.Inference;
using DeepVisionSharp.Application.Models;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Application.Projects;
using DeepVisionSharp.Domain.Entities;

namespace DeepVisionSharp.Shell.ViewModels.Labeling;

public sealed partial class LabelingSampleRow : ObservableObject
{
    public LabelingSampleRow(
        Guid id,
        string fileName,
        int index,
        bool isActive,
        bool isAnnotated,
        bool hasError,
        string? errorMessage,
        string imagePath)
    {
        Id = id;
        FileName = fileName;
        Index = index;
        ImagePath = imagePath;
        HasError = hasError;
        ErrorMessage = errorMessage;
        _isActive = isActive;
        _isAnnotated = isAnnotated;
    }

    public Guid Id { get; }

    public string FileName { get; }

    public int Index { get; }

    public string ImagePath { get; }

    public bool HasError { get; }

    public string? ErrorMessage { get; }

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private bool _isAnnotated;

    [ObservableProperty]
    private bool _isThumbnailLoading;

    [ObservableProperty]
    private ImageSource? _thumbnail;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isAnchor;

    public event Action<LabelingSampleRow>? SelectionChanged;

    partial void OnIsSelectedChanged(bool value)
    {
        SelectionChanged?.Invoke(this);
    }

    public static LabelingSampleRow FromSample(
        GallerySample sample,
        int index,
        bool isActive,
        string imagePath)
    {
        return new LabelingSampleRow(
            sample.Id,
            sample.FileName,
            index,
            isActive,
            sample.IsAnnotated,
            sample.HasError,
            sample.ErrorMessage,
            imagePath);
    }
}

public sealed record LabelOptionRow(Guid Id, string Name, string Color, string HotkeyText)
{
    public static LabelOptionRow FromOption(ProjectLabelOption option, int index)
    {
        var hotkey = index < 9 ? (index + 1).ToString() : index == 9 ? "0" : string.Empty;
        return new LabelOptionRow(option.Id, option.Name, option.Color, hotkey);
    }
}

/// <summary>Labeling auto-inference device row (CPU / CUDA GPU).</summary>
public sealed record InferenceDeviceOption(string Device, string DisplayName)
{
    public static InferenceDeviceOption Cpu { get; } = new(InferenceDevices.Cpu, "CPU");

    public static InferenceDeviceOption PrimaryGpu(string? gpuName = null)
    {
        var label = string.IsNullOrWhiteSpace(gpuName)
            ? "GPU（主设备 cuda:0）"
            : $"GPU · {gpuName.Trim()}";
        return new InferenceDeviceOption(InferenceDevices.PrimaryGpu, label);
    }
}

public sealed record InferenceModelOption(
    Guid Id,
    string Name,
    string ModelType,
    string CheckpointPath,
    int? InputSize = null)
{
    public string DisplayText => $"{Name} ({ModelTypeNames.GetDisplayLabel(ModelType)})";

    public bool IsClassificationModel => ModelTypeNames.IsClassificationModelType(ModelType);

    public string SpatialPolicySummary =>
        InputSize is > 0
            ? $"整图分类 · 输入 {InputSize}"
            : "整图分类（输入尺寸来自模型 sidecar）";

    public override string ToString() => DisplayText;
}
