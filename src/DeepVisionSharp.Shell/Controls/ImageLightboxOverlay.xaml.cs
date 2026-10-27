using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;

namespace DeepVisionSharp.Shell.Controls;

public partial class ImageLightboxOverlay : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty ImageSourceProperty =
        DependencyProperty.Register(
            nameof(ImageSource),
            typeof(ImageSource),
            typeof(ImageLightboxOverlay),
            new PropertyMetadata(null, OnImageSourceChanged));

    public static readonly DependencyProperty CloseCommandProperty =
        DependencyProperty.Register(
            nameof(CloseCommand),
            typeof(ICommand),
            typeof(ImageLightboxOverlay));

    private const double MinScale = 0.25;
    private const double MaxScale = 12.0;
    private const double ViewportPadding = 32;

    private bool _isPanning;
    private Point _panStart;
    private double _translateStartX;
    private double _translateStartY;
    private double _baseLeft;
    private double _baseTop;

    public ImageLightboxOverlay()
    {
        InitializeComponent();
        IsVisibleChanged += OnIsVisibleChanged;
    }

    public ImageSource? ImageSource
    {
        get => (ImageSource?)GetValue(ImageSourceProperty);
        set => SetValue(ImageSourceProperty, value);
    }

    public ICommand? CloseCommand
    {
        get => (ICommand?)GetValue(CloseCommandProperty);
        set => SetValue(CloseCommandProperty, value);
    }

    private static void OnImageSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ImageLightboxOverlay overlay)
        {
            overlay.ScheduleCenterContent();
        }
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            ScheduleCenterContent();
            Dispatcher.BeginInvoke(
                () => Focus(),
                System.Windows.Threading.DispatcherPriority.Input);
        }
        else
        {
            EndPan();
        }
    }

    private void OnViewportSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (IsVisible)
        {
            // Keep the image centered when the overlay is resized (window chrome etc.).
            CenterContent();
        }
    }

    private void ScheduleCenterContent()
    {
        if (!IsVisible)
        {
            return;
        }

        Dispatcher.BeginInvoke(
            CenterContent,
            System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void CenterContent()
    {
        EndPan();
        ImageScale.ScaleX = 1;
        ImageScale.ScaleY = 1;
        ImageTranslate.X = 0;
        ImageTranslate.Y = 0;

        if (!TryGetSourcePixelSize(out var sourceWidth, out var sourceHeight)
            || Viewport.ActualWidth <= 0
            || Viewport.ActualHeight <= 0)
        {
            return;
        }

        var availableWidth = Math.Max(1, Viewport.ActualWidth - ViewportPadding * 2);
        var availableHeight = Math.Max(1, Viewport.ActualHeight - ViewportPadding * 2);
        var fit = Math.Min(availableWidth / sourceWidth, availableHeight / sourceHeight);
        // Never upscale beyond native pixels on open; still allow later mouse-wheel zoom.
        fit = Math.Min(fit, 1.0);

        var displayWidth = sourceWidth * fit;
        var displayHeight = sourceHeight * fit;

        LightboxImage.Width = displayWidth;
        LightboxImage.Height = displayHeight;

        _baseLeft = (Viewport.ActualWidth - displayWidth) / 2.0;
        _baseTop = (Viewport.ActualHeight - displayHeight) / 2.0;
        Canvas.SetLeft(LightboxImage, _baseLeft);
        Canvas.SetTop(LightboxImage, _baseTop);
    }

    private bool TryGetSourcePixelSize(out double width, out double height)
    {
        width = 0;
        height = 0;
        var source = ImageSource ?? LightboxImage.Source;
        switch (source)
        {
            case BitmapSource bitmap when bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0:
                width = bitmap.PixelWidth;
                height = bitmap.PixelHeight;
                return true;
            case ImageSource img when img.Width > 0 && img.Height > 0:
                width = img.Width;
                height = img.Height;
                return true;
            default:
                return false;
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        RequestClose();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            RequestClose();
            e.Handled = true;
        }
    }

    private void RequestClose()
    {
        if (CloseCommand?.CanExecute(null) == true)
        {
            CloseCommand.Execute(null);
        }
    }

    private void OnViewportMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (LightboxImage.Source is null)
        {
            return;
        }

        var anchor = e.GetPosition(Viewport);
        var factor = e.Delta > 0 ? 1.15 : 1 / 1.15;
        ZoomAt(anchor, factor);
        e.Handled = true;
    }

    private void OnViewportMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (LightboxImage.Source is null)
        {
            return;
        }

        _isPanning = true;
        _panStart = e.GetPosition(Viewport);
        _translateStartX = ImageTranslate.X;
        _translateStartY = ImageTranslate.Y;
        Viewport.CaptureMouse();
        e.Handled = true;
    }

    private void OnViewportMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isPanning)
        {
            return;
        }

        EndPan();
        e.Handled = true;
    }

    private void OnViewportMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPanning || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var current = e.GetPosition(Viewport);
        ImageTranslate.X = _translateStartX + (current.X - _panStart.X);
        ImageTranslate.Y = _translateStartY + (current.Y - _panStart.Y);
        e.Handled = true;
    }

    private void EndPan()
    {
        if (!_isPanning)
        {
            return;
        }

        _isPanning = false;
        if (Viewport.IsMouseCaptured)
        {
            Viewport.ReleaseMouseCapture();
        }
    }

    private void ZoomAt(Point anchorInViewport, double factor)
    {
        var currentScale = ImageScale.ScaleX;
        var nextScale = Math.Clamp(currentScale * factor, MinScale, MaxScale);
        if (Math.Abs(nextScale - currentScale) < 1e-6)
        {
            return;
        }

        // Image origin in viewport = base layout position + pan translate.
        // Scale is about the image top-left (0,0).
        var originX = _baseLeft + ImageTranslate.X;
        var originY = _baseTop + ImageTranslate.Y;
        var ratio = nextScale / currentScale;

        // Keep the viewport point under the cursor fixed.
        var newOriginX = anchorInViewport.X - ((anchorInViewport.X - originX) * ratio);
        var newOriginY = anchorInViewport.Y - ((anchorInViewport.Y - originY) * ratio);

        ImageTranslate.X = newOriginX - _baseLeft;
        ImageTranslate.Y = newOriginY - _baseTop;
        ImageScale.ScaleX = nextScale;
        ImageScale.ScaleY = nextScale;
    }
}
