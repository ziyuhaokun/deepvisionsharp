using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using DeepVisionSharp.Application.Inference;
using DeepVisionSharp.Shell.ViewModels.Labeling;

namespace DeepVisionSharp.Shell.ViewModels.Inference;

public sealed partial class InferenceCanvasViewModel : ObservableObject
{
    public ObservableCollection<InferenceDetectionRow> ResultRows { get; } = new();

    /// <summary>Read-only box layer for detection results.</summary>
    public ObservableCollection<BoxShapeViewModel> BoxOverlays { get; } = new();

    [ObservableProperty]
    private double _imageWidth;

    [ObservableProperty]
    private double _imageHeight;

    public bool HasBoxOverlays => BoxOverlays.Count > 0;

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
        BoxOverlays.Clear();
        SelectedResultRow = null;
        OnPropertyChanged(nameof(ShowResultRowList));
        OnPropertyChanged(nameof(HasBoxOverlays));
    }

    public void ApplyResult(InferenceResultSummary summary, object? raw)
    {
        ClearOverlays();

        foreach (var row in summary.Rows)
        {
            ResultRows.Add(row);
            if (row.HasBox)
            {
                BoxOverlays.Add(new BoxShapeViewModel(row.ClassName, row.X, row.Y, row.Width, row.Height));
            }
        }

        ImageWidth = PreviewImage?.Width ?? 0;
        ImageHeight = PreviewImage?.Height ?? 0;
        OnPropertyChanged(nameof(ShowResultRowList));
        OnPropertyChanged(nameof(HasBoxOverlays));
    }
}
