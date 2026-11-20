using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using DeepVisionSharp.Shell.Avalonia.ViewModels;

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

/// <summary>新建多边形的事件参数（图像坐标系）。</summary>
public sealed class SegmentationPolygonCreatedEventArgs(IReadOnlyList<Point> polygon) : EventArgs
{
    public IReadOnlyList<Point> Polygon { get; } = polygon;
}

/// <summary>多边形编辑完成的事件参数。</summary>
public sealed class SegmentationPolygonEditCompletedEventArgs(IReadOnlyList<Point> changed) : EventArgs
{
    public IReadOnlyList<Point> Changed { get; } = changed;
}

/// <summary>
/// 标注画布：把 Workspace 的图像按「适应窗口 × ZoomScale」画进控件，再叠上标注框与多边形。
/// 本波只做静态渲染与缩放/适配，绘制与编辑手势、选择协调器留到接真实 ViewModel 时再补。
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

    public static readonly StyledProperty<bool> IsInstanceSegmentationProperty =
        AvaloniaProperty.Register<AnnotationCanvas, bool>(nameof(IsInstanceSegmentation));

    public static readonly StyledProperty<IReadOnlyList<SampleAnnotationRow>?> AnnotationsProperty =
        AvaloniaProperty.Register<AnnotationCanvas, IReadOnlyList<SampleAnnotationRow>?>(nameof(Annotations));

    public static readonly StyledProperty<IImage?> ImageSourceProperty =
        AvaloniaProperty.Register<AnnotationCanvas, IImage?>(nameof(ImageSource));

    public static readonly StyledProperty<IReadOnlyList<SamplePolygonRow>?> SegmentationPolygonsProperty =
        AvaloniaProperty.Register<AnnotationCanvas, IReadOnlyList<SamplePolygonRow>?>(nameof(SegmentationPolygons));

    public static readonly StyledProperty<bool> ShowLabelNamesProperty =
        AvaloniaProperty.Register<AnnotationCanvas, bool>(nameof(ShowLabelNames), true);

    public static readonly StyledProperty<LabelingToolMode> ToolModeProperty =
        AvaloniaProperty.Register<AnnotationCanvas, LabelingToolMode>(nameof(ToolMode));

    public static readonly StyledProperty<bool> UseTransparentFillProperty =
        AvaloniaProperty.Register<AnnotationCanvas, bool>(nameof(UseTransparentFill));

    public static readonly StyledProperty<double> ZoomScaleProperty =
        AvaloniaProperty.Register<AnnotationCanvas, double>(nameof(ZoomScale), 1d);

    private Vector _pan;

    static AnnotationCanvas()
    {
        AffectsRender<AnnotationCanvas>(
            ActiveColorProperty,
            ActiveLabelIdProperty,
            ActiveLabelProperty,
            IsInstanceSegmentationProperty,
            AnnotationsProperty,
            ImageSourceProperty,
            SegmentationPolygonsProperty,
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

    // 以下四个是画布的绘制/编辑接线点：本波没有绘制与编辑手势，页面照旧接上，事件保留为空。
#pragma warning disable CS0067
    public event EventHandler<AnnotationCreatedEventArgs>? AnnotationCreated;

    public event EventHandler<SegmentationPolygonCreatedEventArgs>? SegmentationPolygonCreated;

    public event EventHandler<SegmentationPolygonEditCompletedEventArgs>? SegmentationPolygonEditCompleted;

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

    public bool IsInstanceSegmentation
    {
        get => GetValue(IsInstanceSegmentationProperty);
        set => SetValue(IsInstanceSegmentationProperty, value);
    }

    public IReadOnlyList<SampleAnnotationRow>? Annotations
    {
        get => GetValue(AnnotationsProperty);
        set => SetValue(AnnotationsProperty, value);
    }

    public IImage? ImageSource
    {
        get => GetValue(ImageSourceProperty);
        set => SetValue(ImageSourceProperty, value);
    }

    public IReadOnlyList<SamplePolygonRow>? SegmentationPolygons
    {
        get => GetValue(SegmentationPolygonsProperty);
        set => SetValue(SegmentationPolygonsProperty, value);
    }

    public bool ShowLabelNames
    {
        get => GetValue(ShowLabelNamesProperty);
        set => SetValue(ShowLabelNamesProperty, value);
    }

    public LabelingToolMode ToolMode
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

        DrawPolygons(context, scale, drawn);
        DrawAnnotations(context, scale, drawn);
    }

    private void DrawPolygons(DrawingContext context, double scale, Rect drawn)
    {
        if (SegmentationPolygons is not { Count: > 0 } polygons)
        {
            return;
        }

        foreach (var polygon in polygons)
        {
            if (polygon.Points.Count < 3)
            {
                continue;
            }

            var points = polygon.Points
                .Select(point => new Point(drawn.Left + (point.X * scale), drawn.Top + (point.Y * scale)))
                .ToList();

            var color = polygon.Color is ISolidColorBrush solid
                ? solid.Color
                : Color.FromRgb(0xF5, 0x9E, 0x0B);

            AnnotationRenderer.DrawCanvasQuad(
                context,
                points,
                points.Aggregate(new Rect(points[0], points[0]), (rect, p) => rect.Union(new Rect(p, p))),
                color,
                selected: false,
                primary: false,
                showLabel: ShowLabelNames,
                text: polygon.Label,
                handleSize: HandleSize,
                transparentFill: UseTransparentFill);
        }
    }

    private void DrawAnnotations(DrawingContext context, double scale, Rect drawn)
    {
        if (Annotations is not { Count: > 0 } rows)
        {
            return;
        }

        var placer = new AnnotationLabelPlacer();
        foreach (var row in rows)
        {
            var screenRect = new Rect(
                drawn.Left + (row.Bounds.X * scale),
                drawn.Top + (row.Bounds.Y * scale),
                row.Bounds.Width * scale,
                row.Bounds.Height * scale);

            var color = row.Color is ISolidColorBrush solid
                ? solid.Color
                : Color.FromRgb(0x3B, 0x82, 0xF6);

            AnnotationRenderer.DrawCanvasAnnotation(
                context,
                screenRect,
                color,
                selected: row.IsSelected,
                primary: row.IsSelected,
                showLabel: ShowLabelNames,
                label: row.Label,
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
