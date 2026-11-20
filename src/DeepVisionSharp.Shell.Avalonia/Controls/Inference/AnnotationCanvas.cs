using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace DeepVisionSharp.Shell.Avalonia.Controls.Inference;

/// <summary>
/// 推理页的只读图像叠加画布：画底、图像、检测框、分割多边形，支持适应窗口与缩放。
/// 视口换算常量（0.05~32 倍缩、28px 适配留边）取自 deepvision 的 AnnotationViewport；
/// 指针交互（画框、拖动、选中、滚轮平移）不在本版范围内。
/// </summary>
public sealed class AnnotationCanvas : Control
{
    private const double MinScale = 0.05;
    private const double MaxScale = 32;
    private const double FitPadding = 28;

    public static readonly StyledProperty<object?> ImageSourceProperty =
        AvaloniaProperty.Register<AnnotationCanvas, object?>(nameof(ImageSource));

    public static readonly StyledProperty<IEnumerable<AnnotationRow>?> AnnotationsProperty =
        AvaloniaProperty.Register<AnnotationCanvas, IEnumerable<AnnotationRow>?>(nameof(Annotations));

    public static readonly StyledProperty<IEnumerable<SegmentationPolygonRow>?> SegmentationPolygonsProperty =
        AvaloniaProperty.Register<AnnotationCanvas, IEnumerable<SegmentationPolygonRow>?>(nameof(SegmentationPolygons));

    public static readonly StyledProperty<bool> ShowLabelNamesProperty =
        AvaloniaProperty.Register<AnnotationCanvas, bool>(nameof(ShowLabelNames), true);

    public static readonly StyledProperty<double> ZoomScaleProperty =
        AvaloniaProperty.Register<AnnotationCanvas, double>(
            nameof(ZoomScale),
            1d,
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<bool> UseTransparentFillProperty =
        AvaloniaProperty.Register<AnnotationCanvas, bool>(nameof(UseTransparentFill));

    public static readonly StyledProperty<bool> IsReadOnlyProperty =
        AvaloniaProperty.Register<AnnotationCanvas, bool>(nameof(IsReadOnly));

    public static readonly StyledProperty<bool> ShowPlaceholderWhenEmptyProperty =
        AvaloniaProperty.Register<AnnotationCanvas, bool>(nameof(ShowPlaceholderWhenEmpty), true);

    /// <summary>与 deepvision 页面属性对齐；只读画布没有指针状态机，此属性当前不参与绘制。</summary>
    public static readonly StyledProperty<string> ToolModeProperty =
        AvaloniaProperty.Register<AnnotationCanvas, string>(nameof(ToolMode), "select");

    private double _scale = 1d;
    private double _panX;
    private double _panY;
    private bool _viewportInitialized;
    private IBrush? _backgroundBrush;

    static AnnotationCanvas()
    {
        AffectsRender<AnnotationCanvas>(
            ImageSourceProperty,
            AnnotationsProperty,
            SegmentationPolygonsProperty,
            ShowLabelNamesProperty,
            UseTransparentFillProperty,
            ShowPlaceholderWhenEmptyProperty);

        ImageSourceProperty.Changed.AddClassHandler<AnnotationCanvas>((control, _) => control.OnImageChanged());
    }

    public AnnotationCanvas()
    {
        Focusable = true;
        ClipToBounds = true;
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.None);
    }

    public event EventHandler? ViewportChanged;

    public object? ImageSource
    {
        get => GetValue(ImageSourceProperty);
        set => SetValue(ImageSourceProperty, value);
    }

    public IEnumerable<AnnotationRow>? Annotations
    {
        get => GetValue(AnnotationsProperty);
        set => SetValue(AnnotationsProperty, value);
    }

    public IEnumerable<SegmentationPolygonRow>? SegmentationPolygons
    {
        get => GetValue(SegmentationPolygonsProperty);
        set => SetValue(SegmentationPolygonsProperty, value);
    }

    public bool ShowLabelNames
    {
        get => GetValue(ShowLabelNamesProperty);
        set => SetValue(ShowLabelNamesProperty, value);
    }

    public double ZoomScale
    {
        get => GetValue(ZoomScaleProperty);
        set => SetValue(ZoomScaleProperty, value);
    }

    public bool UseTransparentFill
    {
        get => GetValue(UseTransparentFillProperty);
        set => SetValue(UseTransparentFillProperty, value);
    }

    public bool IsReadOnly
    {
        get => GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    public bool ShowPlaceholderWhenEmpty
    {
        get => GetValue(ShowPlaceholderWhenEmptyProperty);
        set => SetValue(ShowPlaceholderWhenEmptyProperty, value);
    }

    public string ToolMode
    {
        get => GetValue(ToolModeProperty);
        set => SetValue(ToolModeProperty, value);
    }

    public void FitToView()
    {
        var (width, height) = GetImageSize();
        if (!TryFitToView(width, height, Bounds.Width, Bounds.Height))
        {
            return;
        }

        SetCurrentValue(ZoomScaleProperty, _scale);
        InvalidateVisual();
        ViewportChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ZoomIn() => ZoomAt(new Point(Bounds.Width / 2, Bounds.Height / 2), 1.2);

    public void ZoomOut() => ZoomAt(new Point(Bounds.Width / 2, Bounds.Height / 2), 1 / 1.2);

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        _backgroundBrush ??= FindBrush("IvSurface3Brush", new SolidColorBrush(Color.FromRgb(246, 247, 250)));
        context.DrawRectangle(_backgroundBrush, null, new Rect(Bounds.Size));

        if (ImageSource is not IImage image)
        {
            if (ShowPlaceholderWhenEmpty)
            {
                DrawPlaceholder(context);
            }

            return;
        }

        var (imageWidth, imageHeight) = GetImageSize();
        if (!_viewportInitialized && TryFitToView(imageWidth, imageHeight, Bounds.Width, Bounds.Height))
        {
            // 渲染期不改属性、不发事件：ZoomScale 同步与通知延后到本帧之后。
            Dispatcher.UIThread.Post(
                () =>
                {
                    SetCurrentValue(ZoomScaleProperty, _scale);
                    ViewportChanged?.Invoke(this, EventArgs.Empty);
                },
                DispatcherPriority.Background);
        }

        AnnotationRenderer.DrawImageFrame(context, image, ImageToScreenRect(new Rect(0, 0, imageWidth, imageHeight)));

        var placer = new AnnotationLabelPlacer();
        if (Annotations is { } annotations)
        {
            foreach (var annotation in annotations)
            {
                AnnotationRenderer.DrawBox(
                    context,
                    ImageToScreenRect(annotation.Bounds),
                    AnnotationRenderer.ParseColor(annotation.Color),
                    UseTransparentFill,
                    ShowLabelNames,
                    annotation.Label,
                    placer);
            }
        }

        DrawSegmentationPolygons(context, placer);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (!_viewportInitialized && ImageSource is not null)
        {
            FitToView();
        }
    }

    private void ZoomAt(Point anchor, double factor)
    {
        if (!_viewportInitialized)
        {
            FitToView();
            if (!_viewportInitialized)
            {
                return;
            }
        }

        // 以锚点对应的图像坐标为不动点重算平移，缩放后锚点仍停在原处。
        var imageX = (anchor.X - _panX) / _scale;
        var imageY = (anchor.Y - _panY) / _scale;
        _scale = Math.Clamp(_scale * factor, MinScale, MaxScale);
        _panX = anchor.X - imageX * _scale;
        _panY = anchor.Y - imageY * _scale;

        SetCurrentValue(ZoomScaleProperty, _scale);
        InvalidateVisual();
        ViewportChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool TryFitToView(double imageWidth, double imageHeight, double viewportWidth, double viewportHeight)
    {
        if (imageWidth <= 0 || imageHeight <= 0 || viewportWidth <= 0 || viewportHeight <= 0)
        {
            return false;
        }

        var fittedScale = Math.Min(
            (viewportWidth - FitPadding * 2) / imageWidth,
            (viewportHeight - FitPadding * 2) / imageHeight);
        _scale = Math.Clamp(fittedScale, MinScale, MaxScale);
        _panX = (viewportWidth - imageWidth * _scale) / 2;
        _panY = (viewportHeight - imageHeight * _scale) / 2;
        _viewportInitialized = true;
        return true;
    }

    private void OnImageChanged()
    {
        _viewportInitialized = false;
        _backgroundBrush = null;
        InvalidateVisual();
    }

    private void DrawSegmentationPolygons(DrawingContext context, AnnotationLabelPlacer placer)
    {
        if (SegmentationPolygons is null)
        {
            return;
        }

        foreach (var polygon in SegmentationPolygons)
        {
            if (polygon.Points.Count < 3)
            {
                continue;
            }

            var color = AnnotationRenderer.ParseColor(polygon.Color);
            var screenPoints = polygon.Points.Select(ImagePointToScreen).ToArray();
            var geometry = new StreamGeometry();
            using (var geometryContext = geometry.Open())
            {
                geometryContext.BeginFigure(screenPoints[0], true);
                foreach (var point in screenPoints.Skip(1))
                {
                    geometryContext.LineTo(point);
                }

                geometryContext.EndFigure(true);
            }

            context.DrawGeometry(
                new SolidColorBrush(Color.FromArgb(64, color.R, color.G, color.B)),
                new Pen(new SolidColorBrush(color), 2),
                geometry);

            if (ShowLabelNames && !IsReadOnly)
            {
                AnnotationRenderer.DrawShapeLabel(
                    context,
                    polygon.Label,
                    color,
                    GetPointsBounds(screenPoints),
                    placer);
            }
        }
    }

    private void DrawPlaceholder(DrawingContext context)
    {
        AnnotationRenderer.DrawCenteredText(
            context,
            "请选择样本开始推理",
            Bounds.Size,
            FindBrush("IvText2Brush", Brushes.Gray),
            16);
    }

    private Point ImagePointToScreen(Point imagePoint)
        => new(_panX + imagePoint.X * _scale, _panY + imagePoint.Y * _scale);

    private Rect ImageToScreenRect(Rect imageRect)
        => new(
            _panX + imageRect.X * _scale,
            _panY + imageRect.Y * _scale,
            imageRect.Width * _scale,
            imageRect.Height * _scale);

    private (double Width, double Height) GetImageSize() => ImageSource switch
    {
        Bitmap bitmap => (bitmap.PixelSize.Width, bitmap.PixelSize.Height),
        IImage image => (image.Size.Width, image.Size.Height),
        _ => (0, 0),
    };

    private IBrush FindBrush(string resourceKey, IBrush fallback)
    {
        if (global::Avalonia.Application.Current?.TryFindResource(resourceKey, out var resource) == true
            && resource is IBrush brush)
        {
            return brush;
        }

        return fallback;
    }

    private static Rect GetPointsBounds(IReadOnlyList<Point> points)
    {
        var left = double.MaxValue;
        var top = double.MaxValue;
        var right = double.MinValue;
        var bottom = double.MinValue;
        foreach (var point in points)
        {
            left = Math.Min(left, point.X);
            top = Math.Min(top, point.Y);
            right = Math.Max(right, point.X);
            bottom = Math.Max(bottom, point.Y);
        }

        return new Rect(left, top, right - left, bottom - top);
    }
}
