using CommunityToolkit.Mvvm.ComponentModel;
using DeepVisionSharp.Domain.ValueObjects;

namespace DeepVisionSharp.Shell.ViewModels.Labeling;

public enum BoxToolMode
{
    Select,
    Draw
}

/// <summary>Editable rectangle bound by the box overlay canvas and the side rail list.</summary>
public sealed partial class BoxShapeViewModel : ObservableObject
{
    public BoxShapeViewModel(string label, double x, double y, double width, double height)
    {
        _label = label;
        _x = x;
        _y = y;
        _width = width;
        _height = height;
    }

    [ObservableProperty]
    private string _label;

    [ObservableProperty]
    private double _x;

    [ObservableProperty]
    private double _y;

    [ObservableProperty]
    private double _width;

    [ObservableProperty]
    private double _height;

    /// <summary>1-based position in the editor list; drawn on the canvas and in the list.</summary>
    [ObservableProperty]
    private int _number;

    [ObservableProperty]
    private bool _isSelected;

    public string Color => PaletteFor(Label);

    public string Summary => $"{Label} · {Width:0}×{Height:0}";

    public string CoordinatesText => $"({X:0}, {Y:0})  {Width:0}×{Height:0}";

    public string NumberText => Number > 0 ? Number.ToString() : string.Empty;

    public AnnotationShape ToShape() =>
        new(Label, AnnotationShapeKind.Rectangle, X, Y, Width, Height);

    public static BoxShapeViewModel FromShape(AnnotationShape shape) =>
        new(shape.Label, shape.X, shape.Y, shape.Width, shape.Height);

    public void SetBounds(double x, double y, double width, double height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    partial void OnLabelChanged(string value)
    {
        OnPropertyChanged(nameof(Color));
        OnPropertyChanged(nameof(Summary));
    }

    partial void OnNumberChanged(int value) => OnPropertyChanged(nameof(NumberText));

    partial void OnXChanged(double value) => NotifyGeometryChanged();

    partial void OnYChanged(double value) => NotifyGeometryChanged();

    partial void OnWidthChanged(double value) => NotifyGeometryChanged();

    partial void OnHeightChanged(double value) => NotifyGeometryChanged();

    private void NotifyGeometryChanged()
    {
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(CoordinatesText));
    }

    /// <summary>Stable palette index (FNV-1a) so the same label keeps its colour across sessions.</summary>
    private static string PaletteFor(string label)
    {
        uint hash = 2166136261;
        foreach (var character in label)
        {
            hash ^= character;
            hash *= 16777619;
        }

        return LabelingConstants.Palette[(int)(hash % (uint)LabelingConstants.Palette.Length)];
    }
}
