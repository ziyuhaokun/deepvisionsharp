using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using DeepVisionSharp.Presentation.ViewModels.Labeling;

namespace DeepVisionSharp.Shell.Avalonia.Controls.Labeling;

/// <summary>鸟瞰图点击导航的事件参数。</summary>
public sealed class OverviewNavigateEventArgs(Point imagePoint) : EventArgs
{
    public Point ImagePoint { get; } = imagePoint;
}

/// <summary>
/// 鸟瞰图：把整张图缩进小方框里，叠加 <see cref="Boxes"/> 里的全部框与当前视口框。
/// 框与主画布、侧栏框列表同源（<see cref="BoxShapeViewModel"/> 集合），集合增删与单框几何变化都会重画。
/// </summary>
public sealed class AnnotationOverview : Control
{
    public static readonly StyledProperty<IReadOnlyList<BoxShapeViewModel>?> BoxesProperty =
        AvaloniaProperty.Register<AnnotationOverview, IReadOnlyList<BoxShapeViewModel>?>(nameof(Boxes));

    public static readonly StyledProperty<IImage?> ImageSourceProperty =
        AvaloniaProperty.Register<AnnotationOverview, IImage?>(nameof(ImageSource));

    public static readonly StyledProperty<double> ImageWidthProperty =
        AvaloniaProperty.Register<AnnotationOverview, double>(nameof(ImageWidth));

    public static readonly StyledProperty<double> ImageHeightProperty =
        AvaloniaProperty.Register<AnnotationOverview, double>(nameof(ImageHeight));

    public static readonly StyledProperty<Rect> ViewportProperty =
        AvaloniaProperty.Register<AnnotationOverview, Rect>(nameof(Viewport));

    private readonly List<BoxShapeViewModel> _trackedBoxes = new();

    static AnnotationOverview()
    {
        AffectsRender<AnnotationOverview>(
            BoxesProperty, ImageSourceProperty, ImageWidthProperty, ImageHeightProperty, ViewportProperty);
    }

    public AnnotationOverview()
    {
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    /// <summary>点击鸟瞰图时给出对应的图像坐标，页面据此把主画布居中到该点。</summary>
    public event EventHandler<OverviewNavigateEventArgs>? NavigateRequested;

    /// <summary>当前样本的框（图像坐标），与主画布、侧栏框列表是同一份集合。</summary>
    public IReadOnlyList<BoxShapeViewModel>? Boxes
    {
        get => GetValue(BoxesProperty);
        set => SetValue(BoxesProperty, value);
    }

    public IImage? ImageSource
    {
        get => GetValue(ImageSourceProperty);
        set => SetValue(ImageSourceProperty, value);
    }

    /// <summary>与主画布共用的真实图像坐标尺寸。</summary>
    public double ImageWidth
    {
        get => GetValue(ImageWidthProperty);
        set => SetValue(ImageWidthProperty, value);
    }

    public double ImageHeight
    {
        get => GetValue(ImageHeightProperty);
        set => SetValue(ImageHeightProperty, value);
    }

    /// <summary>当前主画布可见区域（图像坐标系）。</summary>
    public Rect Viewport
    {
        get => GetValue(ViewportProperty);
        set => SetValue(ViewportProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == BoxesProperty)
        {
            TrackBoxes(change.GetOldValue<IReadOnlyList<BoxShapeViewModel>?>(),
                change.GetNewValue<IReadOnlyList<BoxShapeViewModel>?>());
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (ImageSource is null || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        var pixel = ImageSize;
        var scale = Math.Min(Bounds.Width / pixel.Width, Bounds.Height / pixel.Height);
        if (scale <= 0)
        {
            return;
        }

        var left = (Bounds.Width - (pixel.Width * scale)) / 2;
        var top = (Bounds.Height - (pixel.Height * scale)) / 2;
        var position = e.GetPosition(this);
        NavigateRequested?.Invoke(
            this,
            new OverviewNavigateEventArgs(
                new Point((position.X - left) / scale, (position.Y - top) / scale)));
        e.Handled = true;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var bounds = new Rect(Bounds.Size);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var image = ImageSize;

        if (image.Width <= 0 || image.Height <= 0)
        {
            return;
        }

        var scale = Math.Min(bounds.Width / image.Width, bounds.Height / image.Height);
        var width = image.Width * scale;
        var height = image.Height * scale;
        var drawn = new Rect((bounds.Width - width) / 2, (bounds.Height - height) / 2, width, height);

        if (ImageSource is { } source)
        {
            context.DrawImage(source, drawn);
        }

        if (Boxes is { Count: > 0 } boxes)
        {
            foreach (var box in boxes)
            {
                var rect = new Rect(
                    drawn.Left + (box.X * scale),
                    drawn.Top + (box.Y * scale),
                    box.Width * scale,
                    box.Height * scale);

                AnnotationRenderer.DrawOverviewAnnotation(context, rect, AnnotationRenderer.ParseColor(box.Color));
            }
        }

        if (Viewport is { Width: > 0, Height: > 0 } viewport)
        {
            var indicator = new Rect(
                drawn.Left + (viewport.X * scale),
                drawn.Top + (viewport.Y * scale),
                viewport.Width * scale,
                viewport.Height * scale);

            AnnotationRenderer.DrawViewportIndicator(context, indicator);
        }
    }

    private Size ImageSize => new(Math.Max(0, ImageWidth), Math.Max(0, ImageHeight));

    /// <summary>换了一份框集合：把变化通知接到新集合与新集合里的每个框上。</summary>
    private void TrackBoxes(
        IReadOnlyList<BoxShapeViewModel>? previous,
        IReadOnlyList<BoxShapeViewModel>? current)
    {
        if (previous is INotifyCollectionChanged oldCollection)
        {
            oldCollection.CollectionChanged -= OnBoxesCollectionChanged;
        }

        SyncTrackedBoxes(current);

        if (current is INotifyCollectionChanged newCollection)
        {
            newCollection.CollectionChanged += OnBoxesCollectionChanged;
        }

        InvalidateVisual();
    }

    private void SyncTrackedBoxes(IReadOnlyList<BoxShapeViewModel>? boxes)
    {
        foreach (var box in _trackedBoxes)
        {
            box.PropertyChanged -= OnBoxPropertyChanged;
        }

        _trackedBoxes.Clear();
        if (boxes is null)
        {
            return;
        }

        foreach (var box in boxes)
        {
            box.PropertyChanged += OnBoxPropertyChanged;
            _trackedBoxes.Add(box);
        }
    }

    private void OnBoxesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        SyncTrackedBoxes(Boxes);
        InvalidateVisual();
    }

    private void OnBoxPropertyChanged(object? sender, PropertyChangedEventArgs e) => InvalidateVisual();
}
