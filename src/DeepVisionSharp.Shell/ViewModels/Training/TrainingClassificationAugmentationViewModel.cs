using CommunityToolkit.Mvvm.ComponentModel;

using DeepVisionSharp.Domain.ValueObjects;

namespace DeepVisionSharp.Shell.ViewModels.Training;

/// <summary>
/// Classification flip augmentation form. Owned by <see cref="TrainingConfigViewModel"/>.
/// </summary>
public sealed partial class TrainingClassificationAugmentationViewModel : ObservableObject
{
    public TrainingClassificationAugmentationViewModel()
    {
        Apply(ClassificationAugmentation.Disabled);
    }

    [ObservableProperty] private double _flipLeftRight;
    [ObservableProperty] private double _flipUpDown;

    public ClassificationAugmentation Build() =>
        ClassificationAugmentation.Normalize(
            new ClassificationAugmentation(
                FlipLeftRight: FlipLeftRight,
                FlipUpDown: FlipUpDown));

    public void Apply(ClassificationAugmentation? augmentation)
    {
        var value = ClassificationAugmentation.Normalize(
            augmentation ?? ClassificationAugmentation.Disabled);
        FlipLeftRight = value.FlipLeftRight;
        FlipUpDown = value.FlipUpDown;
    }

    public void ResetToDefaults() => Apply(ClassificationAugmentation.Disabled);

    public string BuildSummary(bool isClassificationModelSelected)
    {
        if (!isClassificationModelSelected)
        {
            return "—";
        }

        var value = Build();
        if (value == ClassificationAugmentation.Disabled)
        {
            return "关闭（无增强）";
        }

        return $"fliplr={value.FlipLeftRight:0.##} · flipud={value.FlipUpDown:0.##}";
    }
}
