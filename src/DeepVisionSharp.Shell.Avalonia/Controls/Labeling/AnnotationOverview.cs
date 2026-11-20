using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using DeepVisionSharp.Shell.Avalonia.ViewModels;

namespace DeepVisionSharp.Shell.Avalonia.Controls.Labeling;

/// <summary>鸟瞰图点击导航的事件参数。</summary>
public sealed class OverviewNavigateEventArgs(Point imagePoint) : EventArgs
{
    public Point ImagePoint { get; } = imagePoint;
}

/// <summary>
/// 鸟瞰图：把整张图缩进小方框里，叠加全部标注框与当前视口框。
/// 本波只做静态渲染，点击导航的事件保留但由页面自行决定要不要用。
/// </summary>
public sealed class AnnotationOverview : Control
{
    public static readonly StyledProperty<IReadOnlyList<SampleAnnotationRow>?> AnnotationsProperty =
        AvaloniaProperty.Register<AnnotationOverview, IReadOnlyList<SampleAnnotationRow>?>(nameof(Annotations));

    public static readonly StyledProperty<IImage?> ImageSourceProperty =
        AvaloniaProperty.Register<AnnotationOverview, IImage?>(nameof(ImageSource));

    public static readonly StyledProperty<Rect> ViewportProperty =
        AvaloniaProperty.Register<AnnotationOverview, Rect>(nameof(Viewport));

    static AnnotationOverview()
    {
        AffectsRender<AnnotationOverview>(AnnotationsProperty, ImageSourceProperty, ViewportProperty);
    }

    public AnnotationOverview()
    {
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    /// <summary>点击鸟瞰图时给出对应的图像坐标，页面据此把主画布居中到该点。</summary>
    public event EventHandler<OverviewNavigateEventArgs>? NavigateRequested;

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

    /// <summary>当前主画布可见区域（图像坐标系）。</summary>
    public Rect Viewport
    {
        get => GetValue(ViewportProperty);
        set => SetValue(ViewportProperty, value);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (ImageSource is not { } source || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        var pixel = source is Bitmap bitmap
            ? new Size(bitmap.PixelSize.Width, bitmap.PixelSize.Height)
            : new Size(1280, 860);

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

        var image = ImageSource is Bitmap bitmap
            ? new Size(bitmap.PixelSize.Width, bitmap.PixelSize.Height)
            : new Size(1280, 860);

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

        if (Annotations is { Count: > 0 } rows)
        {
            foreach (var row in rows)
            {
                var color = row.Color is ISolidColorBrush solid
                    ? solid.Color
                    : Color.FromRgb(0x3B, 0x82, 0xF6);

                var rect = new Rect(
                    drawn.Left + (row.Bounds.X * scale),
                    drawn.Top + (row.Bounds.Y * scale),
                    row.Bounds.Width * scale,
                    row.Bounds.Height * scale);

                AnnotationRenderer.DrawOverviewAnnotation(context, rect, color);
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
}
