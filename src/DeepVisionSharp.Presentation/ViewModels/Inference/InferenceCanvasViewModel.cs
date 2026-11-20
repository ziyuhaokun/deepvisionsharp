using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DeepVisionSharp.Application.Inference;
using DeepVisionSharp.Presentation.ViewModels.Labeling;

namespace DeepVisionSharp.Presentation.ViewModels.Inference;

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
    // 由宿主 shell 提供的图像对象（WPF 是 BitmapImage，Avalonia 是 Bitmap）；这一层不读它的成员。
    private object? _previewImage;

    [ObservableProperty]
    private InferenceDetectionRow? _selectedResultRow;

    [ObservableProperty]
    private string _activeFileName = string.Empty;

    [ObservableProperty]
    private string _imageSizeText = string.Empty;

    /// <summary>画布上是否画检测框标签（推理页左下角「显示标签名称」开关）。</summary>
    [ObservableProperty]
    private bool _showLabelNames = true;

    /// <summary>画布缩放比例；页面在视口变化时写回，画布以 OneWay 读它。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ZoomText))]
    private double _zoomScale = 1;

    public string ZoomText => $"{ZoomScale * 100:0}%";

    public bool HasPreviewImage => PreviewImage is not null;

    public bool ShowResultRowList => ResultRows.Count > 0;

    /// <summary>
    /// Publishes the image object together with its pixel size. The size comes from the decode step:
    /// the opaque shell image object exposes no width/height to this layer.
    /// </summary>
    public void SetPreview(object? image, int pixelWidth, int pixelHeight)
    {
        PreviewImage = image;
        ImageWidth = pixelWidth;
        ImageHeight = pixelHeight;
    }

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

        OnPropertyChanged(nameof(ShowResultRowList));
        OnPropertyChanged(nameof(HasBoxOverlays));
    }
}
