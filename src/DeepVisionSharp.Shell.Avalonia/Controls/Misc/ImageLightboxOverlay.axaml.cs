using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace DeepVisionSharp.Shell.Avalonia.Controls;

public partial class ImageLightboxOverlay : UserControl
{
    public static readonly StyledProperty<object?> ImageSourceProperty =
        AvaloniaProperty.Register<ImageLightboxOverlay, object?>(nameof(ImageSource));

    public static readonly StyledProperty<ICommand?> CloseCommandProperty =
        AvaloniaProperty.Register<ImageLightboxOverlay, ICommand?>(nameof(CloseCommand));

    private const double MinScale = 0.25;
    private const double MaxScale = 12.0;
    private const double ViewportPadding = 32;

    private bool _isPanning;
    private Point _panStart;
    private double _translateStartX;
    private double _translateStartY;
    private double _baseLeft;
    private double _baseTop;

    private readonly ScaleTransform _scale = new(1, 1);
    private readonly TranslateTransform _translate = new(0, 0);

    public ImageLightboxOverlay()
    {
        InitializeComponent();
        LightboxImage.RenderTransform = new TransformGroup { Children = { _scale, _translate } };
        ImageSourceProperty.Changed.AddClassHandler<ImageLightboxOverlay>(
            (overlay, _) => overlay.ScheduleCenterContent());
        IsVisibleProperty.Changed.AddClassHandler<ImageLightboxOverlay>(
            (overlay, args) => overlay.OnVisibilityChanged(args.GetNewValue<bool>()));

        Viewport.SizeChanged += (_, _) =>
        {
            if (IsVisible)
            {
                CenterContent();
            }
        };
        Viewport.PointerWheelChanged += OnViewportPointerWheelChanged;
        Viewport.PointerPressed += OnViewportPointerPressed;
        Viewport.PointerReleased += OnViewportPointerReleased;
        Viewport.PointerMoved += OnViewportPointerMoved;
        Viewport.PointerCaptureLost += (_, _) => _isPanning = false;
        KeyDown += OnPreviewKeyDown;

        var closeButton = Content is Grid grid
            ? grid.Children.OfType<Button>().FirstOrDefault()
            : null;
        if (closeButton is not null)
        {
            closeButton.Click += (_, _) => RequestClose();
        }
    }

    public object? ImageSource
    {
        get => GetValue(ImageSourceProperty);
        set => SetValue(ImageSourceProperty, value);
    }

    public ICommand? CloseCommand
    {
        get => GetValue(CloseCommandProperty);
        set => SetValue(CloseCommandProperty, value);
    }

    private void OnVisibilityChanged(bool isVisible)
    {
        if (isVisible)
        {
            ScheduleCenterContent();
            Dispatcher.UIThread.Post(() => Focus(), DispatcherPriority.Input);
        }
        else
        {
            EndPan();
        }
    }

    private void ScheduleCenterContent()
    {
        if (!IsVisible)
        {
            return;
        }

        Dispatcher.UIThread.Post(CenterContent, DispatcherPriority.Loaded);
    }

    private void CenterContent()
    {
        EndPan();
        _scale.ScaleX = 1;
        _scale.ScaleY = 1;
        _translate.X = 0;
        _translate.Y = 0;

        if (!TryGetSourcePixelSize(out var sourceWidth, out var sourceHeight)
            || Viewport.Bounds.Width <= 0
            || Viewport.Bounds.Height <= 0)
        {
            return;
        }

        var availableWidth = Math.Max(1, Viewport.Bounds.Width - ViewportPadding * 2);
        var availableHeight = Math.Max(1, Viewport.Bounds.Height - ViewportPadding * 2);
        var fit = Math.Min(availableWidth / sourceWidth, availableHeight / sourceHeight);
        fit = Math.Min(fit, 1.0);

        var displayWidth = sourceWidth * fit;
        var displayHeight = sourceHeight * fit;

        LightboxImage.Width = displayWidth;
        LightboxImage.Height = displayHeight;

        _baseLeft = (Viewport.Bounds.Width - displayWidth) / 2.0;
        _baseTop = (Viewport.Bounds.Height - displayHeight) / 2.0;
        Canvas.SetLeft(LightboxImage, _baseLeft);
        Canvas.SetTop(LightboxImage, _baseTop);
    }

    private bool TryGetSourcePixelSize(out double width, out double height)
    {
        width = 0;
        height = 0;
        var source = ImageSource;
        if (source is Bitmap bitmap && bitmap.PixelSize.Width > 0 && bitmap.PixelSize.Height > 0)
        {
            width = bitmap.PixelSize.Width;
            height = bitmap.PixelSize.Height;
            return true;
        }

        return false;
    }

    private void RequestClose()
    {
        if (CloseCommand?.CanExecute(null) == true)
        {
            CloseCommand.Execute(null);
        }
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            RequestClose();
            e.Handled = true;
        }
    }

    private void OnViewportPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (ImageSource is null)
        {
            return;
        }

        var anchor = e.GetPosition(Viewport);
        var factor = e.Delta.Y > 0 ? 1.15 : 1 / 1.15;
        ZoomAt(anchor, factor);
        e.Handled = true;
    }

    private void OnViewportPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ImageSource is null)
        {
            return;
        }

        _isPanning = true;
        _panStart = e.GetPosition(Viewport);
        _translateStartX = _translate.X;
        _translateStartY = _translate.Y;
        e.Pointer.Capture(Viewport);
        e.Handled = true;
    }

    private void OnViewportPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isPanning)
        {
            return;
        }

        EndPan();
        e.Handled = true;
    }

    private void OnViewportPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isPanning)
        {
            return;
        }

        var current = e.GetPosition(Viewport);
        _translate.X = _translateStartX + (current.X - _panStart.X);
        _translate.Y = _translateStartY + (current.Y - _panStart.Y);
        e.Handled = true;
    }

    private void EndPan()
    {
        if (!_isPanning)
        {
            return;
        }

        _isPanning = false;
    }

    private void ZoomAt(Point anchorInViewport, double factor)
    {
        var currentScale = _scale.ScaleX;
        var nextScale = Math.Clamp(currentScale * factor, MinScale, MaxScale);
        if (Math.Abs(nextScale - currentScale) < 1e-6)
        {
            return;
        }

        var originX = _baseLeft + _translate.X;
        var originY = _baseTop + _translate.Y;
        var ratio = nextScale / currentScale;

        var newOriginX = anchorInViewport.X - ((anchorInViewport.X - originX) * ratio);
        var newOriginY = anchorInViewport.Y - ((anchorInViewport.Y - originY) * ratio);

        _translate.X = newOriginX - _baseLeft;
        _translate.Y = newOriginY - _baseTop;
        _scale.ScaleX = nextScale;
        _scale.ScaleY = nextScale;
    }
}