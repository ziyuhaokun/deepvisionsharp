using CommunityToolkit.Mvvm.ComponentModel;
using DeepVisionSharp.Domain.ValueObjects;

namespace DeepVisionSharp.Shell.ViewModels.Training;

public sealed partial class TrainingCheckpointCardViewModel : ObservableObject
{
    public TrainingCheckpointCardViewModel(TrainingCheckpointKind kind, string title)
    {
        Kind = kind;
        Title = title;
    }

    public TrainingCheckpointKind Kind { get; }

    public string Title { get; }

    [ObservableProperty]
    private string _pathText = string.Empty;

    [ObservableProperty]
    private string _metricText = "—";

    [ObservableProperty]
    private string _epochText = string.Empty;

    [ObservableProperty]
    private string _footnote = string.Empty;

    [ObservableProperty]
    private bool _isAvailable;

    public bool HasFootnote => !string.IsNullOrWhiteSpace(Footnote);

    partial void OnFootnoteChanged(string value) => OnPropertyChanged(nameof(HasFootnote));
}
