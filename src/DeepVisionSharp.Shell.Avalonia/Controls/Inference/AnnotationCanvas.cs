using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

using System.Collections.Specialized;

using DeepVisionSharp.Presentation.ViewModels.Labeling;

namespace DeepVisionSharp.Shell.Avalonia.Controls.Inference;

/// <summary>
/// 推理页的只读图像叠加画布：画底、图像、检测框，支持适应窗口与缩放。
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

    /// <summary>检测框叠加层；元素与推理结果画布同源（Presentation 的 <see cref="BoxShapeViewModel"/>）。</summary>
    public static readonly StyledProperty<IEnumerable<BoxShapeViewModel>?> AnnotationsProperty =
        AvaloniaProperty.Register<AnnotationCanvas, IEnumerable<BoxShapeViewModel>?>(nameof(Annotations));

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
    private INotifyCollectionChanged? _watchedAnnotations;

    static AnnotationCanvas()
    {
        AffectsRender<AnnotationCanvas>(
            ImageSourceProperty,
            AnnotationsProperty,
            ShowLabelNamesProperty,
            UseTransparentFillProperty,
            ShowPlaceholderWhenEmptyProperty);

        ImageSourceProperty.Changed.AddClassHandler<AnnotationCanvas>((control, _) => control.OnImageChanged());
        AnnotationsProperty.Changed.AddClassHandler<AnnotationCanvas>((control, _) => control.OnAnnotationsChanged());
    }

    public AnnotationCanvas()
    {
        Focusable = true;
        ClipToBounds = true;
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.None);

        // 背景和占位文字是在 Render 里按资源查的，切深浅色后必须重画，否则一直是旧主题的颜色。
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    public event EventHandler? ViewportChanged;

    public object? ImageSource
    {
        get => GetValue(ImageSourceProperty);
        set => SetValue(ImageSourceProperty, value);
    }

    public IEnumerable<BoxShapeViewModel>? Annotations
    {
        get => GetValue(AnnotationsProperty);
        set => SetValue(AnnotationsProperty, value);
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

        // 不缓存：每帧按当前主题查，深浅色切换立即生效。
        context.DrawRectangle(
            FindBrush("IvSurface3Brush", new SolidColorBrush(Color.FromRgb(246, 247, 250))),
            null,
            new Rect(Bounds.Size));

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
                    ImageToScreenRect(new Rect(annotation.X, annotation.Y, annotation.Width, annotation.Height)),
                    AnnotationRenderer.ParseColor(annotation.Color),
                    UseTransparentFill,
                    ShowLabelNames,
                    annotation.Label,
                    placer);
            }
        }
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
        InvalidateVisual();
    }

    /// <summary>
    /// 推理结果的叠加层是稳定的 ObservableCollection（VM 原地增删），属性本身不变，
    /// 所以还要盯集合变更，否则新结果出来画布不会重画。
    /// </summary>
    private void OnAnnotationsChanged()
    {
        if (_watchedAnnotations is not null)
        {
            _watchedAnnotations.CollectionChanged -= OnAnnotationsCollectionChanged;
        }

        _watchedAnnotations = Annotations as INotifyCollectionChanged;
        if (_watchedAnnotations is not null)
        {
            _watchedAnnotations.CollectionChanged += OnAnnotationsCollectionChanged;
        }

        InvalidateVisual();
    }

    private void OnAnnotationsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();

    private void DrawPlaceholder(DrawingContext context)
    {
        AnnotationRenderer.DrawCenteredText(
            context,
            "请选择样本开始推理",
            Bounds.Size,
            FindBrush("IvText2Brush", Brushes.Gray),
            16);
    }

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
        if (this.TryFindResource(resourceKey, ActualThemeVariant, out var resource)
            && resource is IBrush brush)
        {
            return brush;
        }

        return fallback;
    }
}
