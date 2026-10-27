using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using DeepVisionSharp.Application.Inference;

namespace DeepVisionSharp.Shell.ViewModels.Inference;

public sealed partial class InferenceCanvasViewModel : ObservableObject
{
    public ObservableCollection<InferenceDetectionRow> ResultRows { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreviewImage))]
    private ImageSource? _previewImage;

    [ObservableProperty]
    private InferenceDetectionRow? _selectedResultRow;

    [ObservableProperty]
    private string _activeFileName = string.Empty;

    [ObservableProperty]
    private string _imageSizeText = string.Empty;

    public bool HasPreviewImage => PreviewImage is not null;

    public bool ShowResultRowList => ResultRows.Count > 0;

    public void ClearOverlays()
    {
        ResultRows.Clear();
        SelectedResultRow = null;
        OnPropertyChanged(nameof(ShowResultRowList));
    }

    public void ApplyResult(InferenceResultSummary summary, object? raw)
    {
        ClearOverlays();

        foreach (var row in summary.Rows)
        {
            ResultRows.Add(row);
        }

        OnPropertyChanged(nameof(ShowResultRowList));
    }
}
