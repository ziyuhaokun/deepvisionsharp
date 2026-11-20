using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using DeepVisionSharp.Presentation.ViewModels.Labeling;

namespace DeepVisionSharp.Shell.Avalonia.Controls.Labeling;

/// <summary>新建标注框的事件参数（图像坐标系）。</summary>
public sealed class AnnotationCreatedEventArgs(Rect box) : EventArgs
{
    public Rect Box { get; } = box;
}

/// <summary>标注框编辑完成的事件参数。</summary>
public sealed class AnnotationEditCompletedEventArgs(IReadOnlyList<Rect> snapshots) : EventArgs
{
    public IReadOnlyList<Rect> Snapshots { get; } = snapshots;
}

/// <summary>
/// 标注画布：把 Workspace 的图像按「适应窗口 × ZoomScale」画进控件，再叠上 <see cref="Boxes"/> 里的框。
/// 框来自真实的 <see cref="BoxShapeViewModel"/> 集合（与 WPF <c>BoxOverlayCanvas.Boxes</c> 同一份数据），
/// 集合增删与单框几何变化都会重画。绘制与编辑手势留到接交互层时再补。
/// 几何一律用图像坐标，屏幕坐标由 （fit 比例 + 居中偏移 + 平移）换算。
/// </summary>
public sealed class AnnotationCanvas : Control
{
    private const double MinZoom = 0.05;
    private const double MaxZoom = 16;
    private const double HandleSize = 7;

    public static readonly StyledProperty<IBrush?> ActiveColorProperty =
        AvaloniaProperty.Register<AnnotationCanvas, IBrush?>(nameof(ActiveColor));

    public static readonly StyledProperty<string> ActiveLabelIdProperty =
        AvaloniaProperty.Register<AnnotationCanvas, string>(nameof(ActiveLabelId), string.Empty);

    public static readonly StyledProperty<string> ActiveLabelProperty =
        AvaloniaProperty.Register<AnnotationCanvas, string>(nameof(ActiveLabel), string.Empty);

    public static readonly StyledProperty<IReadOnlyList<BoxShapeViewModel>?> BoxesProperty =
        AvaloniaProperty.Register<AnnotationCanvas, IReadOnlyList<BoxShapeViewModel>?>(nameof(Boxes));

    public static readonly StyledProperty<IImage?> ImageSourceProperty =
        AvaloniaProperty.Register<AnnotationCanvas, IImage?>(nameof(ImageSource));

    public static readonly StyledProperty<bool> ShowLabelNamesProperty =
        AvaloniaProperty.Register<AnnotationCanvas, bool>(nameof(ShowLabelNames), true);

    public static readonly StyledProperty<BoxToolMode> ToolModeProperty =
        AvaloniaProperty.Register<AnnotationCanvas, BoxToolMode>(nameof(ToolMode));

    public static readonly StyledProperty<bool> UseTransparentFillProperty =
        AvaloniaProperty.Register<AnnotationCanvas, bool>(nameof(UseTransparentFill));

    public static readonly StyledProperty<double> ZoomScaleProperty =
        AvaloniaProperty.Register<AnnotationCanvas, double>(nameof(ZoomScale), 1d);

    private readonly List<BoxShapeViewModel> _trackedBoxes = new();

    private Vector _pan;

    static AnnotationCanvas()
    {
        AffectsRender<AnnotationCanvas>(
            ActiveColorProperty,
            ActiveLabelIdProperty,
            ActiveLabelProperty,
            BoxesProperty,
            ImageSourceProperty,
            ShowLabelNamesProperty,
            ToolModeProperty,
            UseTransparentFillProperty,
            ZoomScaleProperty);
    }

    public AnnotationCanvas()
    {
        ClipToBounds = true;
        Focusable = true;
    }

    /// <summary>视口变化（缩放或平移）后触发，页面据此同步鸟瞰图。</summary>
    public event EventHandler? ViewportChanged;

    // 以下三个是画布绘制/编辑手势的接线点：本波没有手势，页面照旧接上，事件保留为空。
#pragma warning disable CS0067
    public event EventHandler<AnnotationCreatedEventArgs>? AnnotationCreated;

    public event EventHandler? AnnotationEditStarted;

    public event EventHandler<AnnotationEditCompletedEventArgs>? AnnotationEditCompleted;
#pragma warning restore CS0067

    public IBrush? ActiveColor
    {
        get => GetValue(ActiveColorProperty);
        set => SetValue(ActiveColorProperty, value);
    }

    public string ActiveLabelId
    {
        get => GetValue(ActiveLabelIdProperty);
        set => SetValue(ActiveLabelIdProperty, value);
    }

    public string ActiveLabel
    {
        get => GetValue(ActiveLabelProperty);
        set => SetValue(ActiveLabelProperty, value);
    }

    /// <summary>当前样本的框（图像坐标），与侧栏框列表是同一份 <see cref="BoxShapeViewModel"/> 集合。</summary>
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

    public bool ShowLabelNames
    {
        get => GetValue(ShowLabelNamesProperty);
        set => SetValue(ShowLabelNamesProperty, value);
    }

    public BoxToolMode ToolMode
    {
        get => GetValue(ToolModeProperty);
        set => SetValue(ToolModeProperty, value);
    }

    public bool UseTransparentFill
    {
        get => GetValue(UseTransparentFillProperty);
        set => SetValue(UseTransparentFillProperty, value);
    }

    /// <summary>在「适应窗口」基础上的缩放倍数；1 = 正好铺满可用区域。</summary>
    public double ZoomScale
    {
        get => GetValue(ZoomScaleProperty);
        set => SetValue(ZoomScaleProperty, Math.Clamp(value, MinZoom, MaxZoom));
    }

    private Size ImagePixelSize => ImageSource is Bitmap bitmap
        ? new Size(bitmap.PixelSize.Width, bitmap.PixelSize.Height)
        : new Size(1280, 860);

    public void ZoomIn() => ZoomScale *= 1.25;

    public void ZoomOut() => ZoomScale /= 1.25;

    /// <summary>缩放回适应窗口并居中。</summary>
    public void FitToView()
    {
        _pan = default;
        ZoomScale = 1;
        RaiseViewportChanged();
    }

    /// <summary>把图像坐标里的一点移到视口中心（鸟瞰图导航用）。</summary>
    public void CenterViewportAt(Point imagePoint)
    {
        var image = ImagePixelSize;
        var scale = EffectiveScale(image);
        if (scale <= 0)
        {
            return;
        }

        var drawn = ContentRect(image, scale);

        // 让 imagePoint 落在视口中心
        _pan = new Vector(
            Bounds.Center.X - (drawn.Left + (imagePoint.X * scale)),
            Bounds.Center.Y - (drawn.Top + (imagePoint.Y * scale)));

        InvalidateVisual();
        RaiseViewportChanged();
    }

    /// <summary>当前可见区域的图像坐标矩形，鸟瞰图画视口框用。</summary>
    public Rect GetViewportImageRect()
    {
        var image = ImagePixelSize;
        var scale = EffectiveScale(image);
        if (scale <= 0)
        {
            return new Rect(0, 0, image.Width, image.Height);
        }

        var drawn = ContentRect(image, scale);
        var left = Math.Max(0, (0 - drawn.Left) / scale);
        var top = Math.Max(0, (0 - drawn.Top) / scale);
        var right = Math.Min(image.Width, (Bounds.Width - drawn.Left) / scale);
        var bottom = Math.Min(image.Height, (Bounds.Height - drawn.Top) / scale);
        return new Rect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    /// <summary>选择协调器属于交互层，本波不接，保留调用点。</summary>
    public void BindSelectionCoordinator(object? coordinator)
    {
    }

    /// <summary>本波没有绘制中的几何，永远返回 false。</summary>
    public bool CancelGeometryEdit() => false;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == BoxesProperty)
        {
            TrackBoxes(change.GetOldValue<IReadOnlyList<BoxShapeViewModel>?>(),
                change.GetNewValue<IReadOnlyList<BoxShapeViewModel>?>());
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(FindBrush("IvImageCanvasBrush", Color.FromRgb(0x16, 0x1A, 0x20)), bounds);

        var image = ImagePixelSize;
        var scale = EffectiveScale(image);
        var drawn = ContentRect(image, scale);

        if (ImageSource is { } source)
        {
            context.DrawImage(source, drawn);
        }

        DrawBoxes(context, scale, drawn);
    }

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

    private void DrawBoxes(DrawingContext context, double scale, Rect drawn)
    {
        if (Boxes is not { Count: > 0 } boxes)
        {
            return;
        }

        var placer = new AnnotationLabelPlacer();
        foreach (var box in boxes)
        {
            var screenRect = new Rect(
                drawn.Left + (box.X * scale),
                drawn.Top + (box.Y * scale),
                box.Width * scale,
                box.Height * scale);

            AnnotationRenderer.DrawCanvasAnnotation(
                context,
                screenRect,
                AnnotationRenderer.ParseColor(box.Color),
                selected: box.IsSelected,
                primary: box.IsSelected,
                showLabel: ShowLabelNames,
                label: box.Label,
                handleSize: HandleSize,
                transparentFill: UseTransparentFill,
                placer: placer);
        }
    }

    private double EffectiveScale(Size image)
    {
        if (image.Width <= 0 || image.Height <= 0 || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return 1;
        }

        var fit = Math.Min(Bounds.Width / image.Width, Bounds.Height / image.Height);
        return fit * ZoomScale;
    }

    private Rect ContentRect(Size image, double scale)
    {
        var width = image.Width * scale;
        var height = image.Height * scale;
        var left = ((Bounds.Width - width) / 2) + _pan.X;
        var top = ((Bounds.Height - height) / 2) + _pan.Y;
        return new Rect(left, top, width, height);
    }

    private static IBrush FindBrush(string key, Color fallback)
        => global::Avalonia.Application.Current is { } app && app.TryFindResource(key, out var value) && value is IBrush brush
            ? brush
            : new SolidColorBrush(fallback);

    private void RaiseViewportChanged() => ViewportChanged?.Invoke(this, EventArgs.Empty);
}
