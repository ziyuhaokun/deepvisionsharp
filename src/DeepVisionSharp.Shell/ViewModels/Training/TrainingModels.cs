using CommunityToolkit.Mvvm.ComponentModel;

using DeepVisionSharp.Application.Models;
using DeepVisionSharp.Domain.ValueObjects;
using DeepVisionSharp.Shell.Automation;

namespace DeepVisionSharp.Shell.ViewModels.Training;

public sealed partial class GalleryOption : ObservableObject
{
    public GalleryOption(
        Guid id,
        string name,
        int sampleCount,
        int annotatedCount)
    {
        Id = id;
        Name = name;
        SampleCount = sampleCount;
        AnnotatedCount = annotatedCount;
    }

    public Guid Id { get; }

    public string Name { get; }

    public string SelectAutomationId => UiAutomationIds.Training.GallerySelect(Id);

    public int SampleCount { get; }

    public int AnnotatedCount { get; }

    /// <summary>Unsupervised anomaly lists sample count only (no annotation requirement).</summary>
    public bool UsesSampleCountDisplay { get; set; }

    public bool IsSelectable => UsesSampleCountDisplay
        ? SampleCount > 0
        : SampleCount > 0 && AnnotatedCount > 0;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private GalleryDatasetRole _role = GalleryDatasetRole.None;

    public string DisplayText => UsesSampleCountDisplay
        ? $"{Name}（{SampleCount}）"
        : $"{Name}（{AnnotatedCount}/{SampleCount}）";

    public override string ToString() => Name;
}

public enum TrainingNumericField
{
    Epochs,
    BatchSize,
    LearningRate,
    ValidationRatio,
    NetworkInputSize,
    NetworkInputWidth,
    NetworkInputHeight
}

public sealed record AutoAugmentOption(string Value, string DisplayText)
{
    public override string ToString() => DisplayText;
}
