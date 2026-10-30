using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

using DeepVisionSharp.Shell.ViewModels.Labeling;

namespace DeepVisionSharp.Shell.Controls;

/// <summary>
/// Rectangle editing layer drawn over the labeling image. Coordinates are original-image pixels;
/// the control maps them through the same Uniform fit the <see cref="System.Windows.Controls.Image"/>
/// uses, with zoom/pan on top, so boxes stay glued to the pixels at any scale.
/// </summary>
public sealed class BoxOverlayCanvas : FrameworkElement
{
    private const double HandleSize = 9;
    private const double HitTolerance = 7;
    private const double MinimumDragPixels = 3;
    private const double MinZoom = 0.15;
    private const double MaxZoom = 8;

    public static readonly DependencyProperty ImageSourceProperty = DependencyProperty.Register(
        nameof(ImageSource), typeof(ImageSource), typeof(BoxOverlayCanvas),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ImageWidthProperty = DependencyProperty.Register(
        nameof(ImageWidth), typeof(double), typeof(BoxOverlayCanvas),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender, OnImageSizeChanged));

    public static readonly DependencyProperty ImageHeightProperty = DependencyProperty.Register(
        nameof(ImageHeight), typeof(double), typeof(BoxOverlayCanvas),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender, OnImageSizeChanged));

    public static readonly DependencyProperty BoxesProperty = DependencyProperty.Register(
        nameof(Boxes), typeof(IList), typeof(BoxOverlayCanvas),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnBoxesChanged));

    public static readonly DependencyProperty SelectedBoxProperty = DependencyProperty.Register(
        nameof(SelectedBox), typeof(BoxShapeViewModel), typeof(BoxOverlayCanvas),
        new FrameworkPropertyMetadata(
            null,
            FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty ActiveLabelProperty = DependencyProperty.Register(
        nameof(ActiveLabel), typeof(string), typeof(BoxOverlayCanvas),
        new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty ToolProperty = DependencyProperty.Register(
        nameof(Tool), typeof(BoxToolMode), typeof(BoxOverlayCanvas),
        new FrameworkPropertyMetadata(BoxToolMode.Draw));

    public static readonly DependencyProperty IsReadOnlyProperty = DependencyProperty.Register(
        nameof(IsReadOnly), typeof(bool), typeof(BoxOverlayCanvas),
        new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty ShowLabelNamesProperty = DependencyProperty.Register(
        nameof(ShowLabelNames), typeof(bool), typeof(BoxOverlayCanvas),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShowBoxNumbersProperty = DependencyProperty.Register(
        nameof(ShowBoxNumbers), typeof(bool), typeof(BoxOverlayCanvas),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ZoomScaleProperty = DependencyProperty.Register(
        nameof(ZoomScale), typeof(double), typeof(BoxOverlayCanvas),
        new FrameworkPropertyMetadata(
            1d,
            FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnZoomChanged,
            CoerceZoom));

    public static readonly DependencyProperty ViewportRectProperty = DependencyProperty.Register(
        nameof(ViewportRect), typeof(Rect), typeof(BoxOverlayCanvas),
        new FrameworkPropertyMetadata(Rect.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty EditStartingCommandProperty = DependencyProperty.Register(
        nameof(EditStartingCommand), typeof(ICommand), typeof(BoxOverlayCanvas));

    public static readonly DependencyProperty EditCompletedCommandProperty = DependencyProperty.Register(
        nameof(EditCompletedCommand), typeof(ICommand), typeof(BoxOverlayCanvas));

    public static readonly DependencyProperty UndoCommandProperty = DependencyProperty.Register(
        nameof(UndoCommand), typeof(ICommand), typeof(BoxOverlayCanvas));

    public static readonly DependencyProperty RedoCommandProperty = DependencyProperty.Register(
        nameof(RedoCommand), typeof(ICommand), typeof(BoxOverlayCanvas));

    private DragMode _dragMode;
    private HandleKind _handle;
    private Point _dragStartImage;
    private Point _dragStartScreen;
    private Rect _originalBounds;
    private BoxShapeViewModel? _draftBox;
    private double _panX;
    private double _panY;
    private bool _isPanning;
    private bool _updatingViewport;

    public BoxOverlayCanvas()
    {
        Focusable = true;
        FocusVisualStyle = null;
        ClipToBounds = true;
    }

    private enum DragMode
    {
        None,
        Draw,
        Move,
        Resize
    }

    private enum HandleKind
    {
        None,
        NorthWest,
        North,
        NorthEast,
        East,
        SouthEast,
        South,
        SouthWest,
        West
    }

    public ImageSource? ImageSource
    {
        get => (ImageSource?)GetValue(ImageSourceProperty);
        set => SetValue(ImageSourceProperty, value);
    }

    public double ImageWidth
    {
        get => (double)GetValue(ImageWidthProperty);
        set => SetValue(ImageWidthProperty, value);
    }

    public double ImageHeight
    {
        get => (double)GetValue(ImageHeightProperty);
        set => SetValue(ImageHeightProperty, value);
    }

    public IList? Boxes
    {
        get => (IList?)GetValue(BoxesProperty);
        set => SetValue(BoxesProperty, value);
    }

    public BoxShapeViewModel? SelectedBox
    {
        get => (BoxShapeViewModel?)GetValue(SelectedBoxProperty);
        set => SetValue(SelectedBoxProperty, value);
    }

    public string? ActiveLabel
    {
        get => (string?)GetValue(ActiveLabelProperty);
        set => SetValue(ActiveLabelProperty, value);
    }

    public BoxToolMode Tool
    {
        get => (BoxToolMode)GetValue(ToolProperty);
        set => SetValue(ToolProperty, value);
    }

    public bool IsReadOnly
    {
        get => (bool)GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    public bool ShowLabelNames
    {
        get => (bool)GetValue(ShowLabelNamesProperty);
        set => SetValue(ShowLabelNamesProperty, value);
    }

    public bool ShowBoxNumbers
    {
        get => (bool)GetValue(ShowBoxNumbersProperty);
        set => SetValue(ShowBoxNumbersProperty, value);
    }

    public double ZoomScale
    {
        get => (double)GetValue(ZoomScaleProperty);
        set => SetValue(ZoomScaleProperty, value);
    }

    public Rect ViewportRect
    {
        get => (Rect)GetValue(ViewportRectProperty);
        set => SetValue(ViewportRectProperty, value);
    }

    public ICommand? EditStartingCommand
    {
        get => (ICommand?)GetValue(EditStartingCommandProperty);
        set => SetValue(EditStartingCommandProperty, value);
    }

    public ICommand? EditCompletedCommand
    {
        get => (ICommand?)GetValue(EditCompletedCommandProperty);
        set => SetValue(EditCompletedCommandProperty, value);
    }

    public ICommand? UndoCommand
    {
        get => (ICommand?)GetValue(UndoCommandProperty);
        set => SetValue(UndoCommandProperty, value);
    }

    public ICommand? RedoCommand
    {
        get => (ICommand?)GetValue(RedoCommandProperty);
        set => SetValue(RedoCommandProperty, value);
    }

    /// <summary>Zooms around the control centre.</summary>
    public void ZoomBy(double factor)
    {
        var centre = new Point(ActualWidth / 2, ActualHeight / 2);
        ZoomAround(centre, factor);
    }

    /// <summary>Returns to the fit-to-window view.</summary>
    public void FitView()
    {
        _panX = 0;
        _panY = 0;
        ZoomScale = 1;
        InvalidateVisual();
        UpdateViewportRect();
        OnPropertyChanged_ViewChanged();
    }

    private event EventHandler? ViewChanged;

    /// <summary>Raised after wheel/middle-drag changed zoom or pan (the toolbar text binds through ZoomScale).</summary>
    public event EventHandler? ViewTransformChanged
    {
        add => ViewChanged += value;
        remove => ViewChanged -= value;
    }

    private void OnPropertyChanged_ViewChanged() => ViewChanged?.Invoke(this, EventArgs.Empty);

    private static object CoerceZoom(DependencyObject d, object baseValue)
    {
        var value = (double)baseValue;
        return double.IsFinite(value) ? Math.Clamp(value, MinZoom, MaxZoom) : 1d;
    }

    private static void OnZoomChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (BoxOverlayCanvas)d;
        control.InvalidateVisual();
        control.UpdateViewportRect();
        control.OnPropertyChanged_ViewChanged();
    }

    private static void OnImageSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (BoxOverlayCanvas)d;
        control._panX = 0;
        control._panY = 0;
        control.ZoomScale = 1;
        control.InvalidateVisual();
        control.UpdateViewportRect();
    }

    private static void OnBoxesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (BoxOverlayCanvas)d;
        if (e.OldValue is INotifyCollectionChanged oldCollection)
        {
            oldCollection.CollectionChanged -= control.OnBoxesCollectionChanged;
        }

        if (e.NewValue is INotifyCollectionChanged newCollection)
        {
            newCollection.CollectionChanged += control.OnBoxesCollectionChanged;
        }

        control.InvalidateVisual();
    }

    private void OnBoxesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        InvalidateVisual();

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        // A transparent fill makes the layer hit-testable; the image underneath stays visible.
        drawingContext.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));

        var content = GetContentRect();
        if (content.Width > 0 && content.Height > 0)
        {
            if (ImageSource is not null)
            {
                drawingContext.DrawImage(ImageSource, content);
            }

            drawingContext.DrawRectangle(null, CreatePen(Color.FromArgb(0x66, 0x94, 0xA3, 0xB8), 1), content);
        }

        if (Boxes is not null && content.Width > 0)
        {
            var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            foreach (var item in Boxes.OfType<BoxShapeViewModel>())
            {
                var rect = ToScreenRect(item);
                var color = ParseColor(item.Color);
                var isSelected = ReferenceEquals(item, SelectedBox);
                drawingContext.DrawRectangle(null, CreatePen(color, isSelected ? 3 : 2), rect);
                if (ShowBoxNumbers && item.Number > 0)
                {
                    DrawBoxNumber(drawingContext, rect, item.Number, color, dpi);
                }

                if (ShowLabelNames)
                {
                    DrawLabel(drawingContext, rect, item.Label, color, dpi);
                }

                if (isSelected && !IsReadOnly)
                {
                    foreach (var handle in HandlePoints(rect))
                    {
                        drawingContext.DrawRectangle(Brushes.White, CreatePen(color, 1),
                            new Rect(handle.X - (HandleSize / 2), handle.Y - (HandleSize / 2), HandleSize, HandleSize));
                    }
                }
            }
        }

        UpdateViewportRect();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        UpdateViewportRect();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (IsReadOnly || ImageWidth <= 0)
        {
            return;
        }

        var factor = e.Delta > 0 ? 1.12 : 1 / 1.12;
        ZoomAround(e.GetPosition(this), factor);
        e.Handled = true;
    }

    private void ZoomAround(Point anchor, double factor)
    {
        var before = ToImagePoint(anchor);
        var target = Math.Clamp(ZoomScale * factor, MinZoom, MaxZoom);
        if (Math.Abs(target - ZoomScale) < 0.0001)
        {
            return;
        }

        ZoomScale = target;
        // Keep the image point under the cursor fixed after the scale changed.
        var content = GetContentRect();
        var scale = content.Width / Math.Max(1, ImageWidth);
        var currentX = content.X + (before.X * scale);
        var currentY = content.Y + (before.Y * scale);
        _panX += anchor.X - currentX;
        _panY += anchor.Y - currentY;
        InvalidateVisual();
        UpdateViewportRect();
        OnPropertyChanged_ViewChanged();
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.ChangedButton == MouseButton.Middle && !IsReadOnly)
        {
            _isPanning = true;
            _dragStartScreen = e.GetPosition(this);
            CaptureMouse();
            Cursor = Cursors.SizeAll;
            e.Handled = true;
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        if (IsReadOnly || Boxes is null)
        {
            return;
        }

        var point = e.GetPosition(this);
        var imagePoint = ToImagePoint(point);

        if (SelectedBox is not null)
        {
            var handle = HitTestHandle(SelectedBox, point);
            if (handle != HandleKind.None)
            {
                BeginEdit();
                _dragMode = DragMode.Resize;
                _handle = handle;
                _dragStartImage = imagePoint;
                _originalBounds = new Rect(SelectedBox.X, SelectedBox.Y, SelectedBox.Width, SelectedBox.Height);
                CaptureMouse();
                e.Handled = true;
                return;
            }
        }

        var hit = HitTestBox(imagePoint);
        if (hit is not null)
        {
            SelectedBox = hit;
            if (Tool == BoxToolMode.Select)
            {
                BeginEdit();
                _dragMode = DragMode.Move;
                _dragStartImage = imagePoint;
                _originalBounds = new Rect(hit.X, hit.Y, hit.Width, hit.Height);
                CaptureMouse();
            }

            e.Handled = true;
            return;
        }

        if (Tool != BoxToolMode.Draw || string.IsNullOrWhiteSpace(ActiveLabel))
        {
            SelectedBox = null;
            return;
        }

        BeginEdit();
        _draftBox = new BoxShapeViewModel(ActiveLabel!, imagePoint.X, imagePoint.Y, 0.5, 0.5);
        Boxes.Add(_draftBox);
        SelectedBox = _draftBox;
        _dragMode = DragMode.Draw;
        _dragStartImage = imagePoint;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_isPanning)
        {
            var current = e.GetPosition(this);
            _panX += current.X - _dragStartScreen.X;
            _panY += current.Y - _dragStartScreen.Y;
            _dragStartScreen = current;
            InvalidateVisual();
            UpdateViewportRect();
            OnPropertyChanged_ViewChanged();
            return;
        }

        if (_dragMode == DragMode.None)
        {
            Cursor = Tool == BoxToolMode.Draw && !IsReadOnly ? Cursors.Cross : Cursors.Arrow;
            return;
        }

        var imagePoint = ToImagePoint(e.GetPosition(this));
        switch (_dragMode)
        {
            case DragMode.Draw when _draftBox is not null:
                var left = Math.Min(_dragStartImage.X, imagePoint.X);
                var top = Math.Min(_dragStartImage.Y, imagePoint.Y);
                var right = Math.Max(_dragStartImage.X, imagePoint.X);
                var bottom = Math.Max(_dragStartImage.Y, imagePoint.Y);
                _draftBox.SetBounds(
                    Math.Clamp(left, 0, ImageWidth),
                    Math.Clamp(top, 0, ImageHeight),
                    Math.Clamp(right, 0, ImageWidth) - Math.Clamp(left, 0, ImageWidth),
                    Math.Clamp(bottom, 0, ImageHeight) - Math.Clamp(top, 0, ImageHeight));
                break;
            case DragMode.Move when SelectedBox is not null:
                var deltaX = imagePoint.X - _dragStartImage.X;
                var deltaY = imagePoint.Y - _dragStartImage.Y;
                var x = Math.Clamp(_originalBounds.X + deltaX, 0, Math.Max(0, ImageWidth - _originalBounds.Width));
                var y = Math.Clamp(_originalBounds.Y + deltaY, 0, Math.Max(0, ImageHeight - _originalBounds.Height));
                SelectedBox.SetBounds(x, y, _originalBounds.Width, _originalBounds.Height);
                break;
            case DragMode.Resize when SelectedBox is not null:
                ResizeSelected(imagePoint);
                break;
        }

        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_dragMode == DragMode.None)
        {
            return;
        }

        if (_dragMode == DragMode.Draw && _draftBox is not null)
        {
            var scale = GetScale();
            var tooSmall = _draftBox.Width * scale < MinimumDragPixels
                || _draftBox.Height * scale < MinimumDragPixels;
            if (tooSmall)
            {
                Boxes?.Remove(_draftBox);
                SelectedBox = null;
                _draftBox = null;
                _dragMode = DragMode.None;
                ReleaseMouseCapture();
                InvalidateVisual();
                return;
            }

            _draftBox = null;
        }

        _dragMode = DragMode.None;
        ReleaseMouseCapture();
        CompleteEdit();
        InvalidateVisual();
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.ChangedButton == MouseButton.Middle && _isPanning)
        {
            _isPanning = false;
            ReleaseMouseCapture();
            Cursor = Cursors.Arrow;
            e.Handled = true;
        }
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _dragMode = DragMode.None;
        _draftBox = null;
        _isPanning = false;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (IsReadOnly)
        {
            return;
        }

        if (e.Key == Key.Delete && SelectedBox is not null)
        {
            BeginEdit();
            Boxes?.Remove(SelectedBox);
            SelectedBox = null;
            CompleteEdit();
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && _dragMode != DragMode.None)
        {
            CancelDrag();
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Z)
        {
            if (UndoCommand?.CanExecute(null) == true)
            {
                UndoCommand.Execute(null);
            }

            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Y)
        {
            if (RedoCommand?.CanExecute(null) == true)
            {
                RedoCommand.Execute(null);
            }

            e.Handled = true;
        }
    }

    private void ResizeSelected(Point imagePoint)
    {
        if (SelectedBox is null)
        {
            return;
        }

        var left = _originalBounds.Left;
        var top = _originalBounds.Top;
        var right = _originalBounds.Right;
        var bottom = _originalBounds.Bottom;

        if (_handle is HandleKind.NorthWest or HandleKind.West or HandleKind.SouthWest)
        {
            left = imagePoint.X;
        }

        if (_handle is HandleKind.NorthEast or HandleKind.East or HandleKind.SouthEast)
        {
            right = imagePoint.X;
        }

        if (_handle is HandleKind.NorthWest or HandleKind.North or HandleKind.NorthEast)
        {
            top = imagePoint.Y;
        }

        if (_handle is HandleKind.SouthWest or HandleKind.South or HandleKind.SouthEast)
        {
            bottom = imagePoint.Y;
        }

        var x = Math.Clamp(Math.Min(left, right), 0, ImageWidth);
        var y = Math.Clamp(Math.Min(top, bottom), 0, ImageHeight);
        var width = Math.Clamp(Math.Abs(right - left), 1, Math.Max(1, ImageWidth - x));
        var height = Math.Clamp(Math.Abs(bottom - top), 1, Math.Max(1, ImageHeight - y));
        SelectedBox.SetBounds(x, y, width, height);
    }

    private void CancelDrag()
    {
        if (_dragMode == DragMode.Draw && _draftBox is not null)
        {
            Boxes?.Remove(_draftBox);
            SelectedBox = null;
        }
        else if (SelectedBox is not null && _dragMode is DragMode.Move or DragMode.Resize)
        {
            SelectedBox.SetBounds(_originalBounds.X, _originalBounds.Y, _originalBounds.Width, _originalBounds.Height);
        }

        _dragMode = DragMode.None;
        _draftBox = null;
        ReleaseMouseCapture();
        InvalidateVisual();
    }

    private void BeginEdit()
    {
        if (EditStartingCommand?.CanExecute(null) == true)
        {
            EditStartingCommand.Execute(null);
        }
    }

    private void CompleteEdit()
    {
        if (EditCompletedCommand?.CanExecute(null) == true)
        {
            EditCompletedCommand.Execute(null);
        }
    }

    private BoxShapeViewModel? HitTestBox(Point imagePoint)
    {
        if (Boxes is null)
        {
            return null;
        }

        BoxShapeViewModel? found = null;
        foreach (var box in Boxes.OfType<BoxShapeViewModel>())
        {
            if (imagePoint.X >= box.X && imagePoint.X <= box.X + box.Width
                && imagePoint.Y >= box.Y && imagePoint.Y <= box.Y + box.Height)
            {
                found = box;
            }
        }

        return found;
    }

    private HandleKind HitTestHandle(BoxShapeViewModel box, Point screenPoint)
    {
        var rect = ToScreenRect(box);
        foreach (var (kind, point) in Handles(rect))
        {
            if (Math.Abs(screenPoint.X - point.X) <= HitTolerance + (HandleSize / 2)
                && Math.Abs(screenPoint.Y - point.Y) <= HitTolerance + (HandleSize / 2))
            {
                return kind;
            }
        }

        return HandleKind.None;
    }

    private static IEnumerable<(HandleKind Kind, Point Point)> Handles(Rect rect) =>
    [
        (HandleKind.NorthWest, rect.TopLeft),
        (HandleKind.North, new Point(rect.Left + (rect.Width / 2), rect.Top)),
        (HandleKind.NorthEast, rect.TopRight),
        (HandleKind.East, new Point(rect.Right, rect.Top + (rect.Height / 2))),
        (HandleKind.SouthEast, rect.BottomRight),
        (HandleKind.South, new Point(rect.Left + (rect.Width / 2), rect.Bottom)),
        (HandleKind.SouthWest, rect.BottomLeft),
        (HandleKind.West, new Point(rect.Left, rect.Top + (rect.Height / 2)))
    ];

    private static IEnumerable<Point> HandlePoints(Rect rect) =>
        Handles(rect).Select(item => item.Point);

    private Rect GetContentRect()
    {
        if (ImageWidth <= 0 || ImageHeight <= 0 || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return Rect.Empty;
        }

        var fit = Math.Min(ActualWidth / ImageWidth, ActualHeight / ImageHeight);
        var scale = fit * ZoomScale;
        var width = ImageWidth * scale;
        var height = ImageHeight * scale;
        return new Rect(
            ((ActualWidth - width) / 2) + _panX,
            ((ActualHeight - height) / 2) + _panY,
            width,
            height);
    }

    private double GetScale()
    {
        var content = GetContentRect();
        return content.Width <= 0 || ImageWidth <= 0 ? 1 : content.Width / ImageWidth;
    }

    private Rect ToScreenRect(BoxShapeViewModel box)
    {
        var content = GetContentRect();
        var scale = GetScale();
        return new Rect(
            content.X + (box.X * scale),
            content.Y + (box.Y * scale),
            Math.Max(1, box.Width * scale),
            Math.Max(1, box.Height * scale));
    }

    private Point ToImagePoint(Point screenPoint)
    {
        var content = GetContentRect();
        var scale = GetScale();
        if (scale <= 0)
        {
            return new Point(0, 0);
        }

        return new Point(
            Math.Clamp((screenPoint.X - content.X) / scale, 0, Math.Max(0, ImageWidth)),
            Math.Clamp((screenPoint.Y - content.Y) / scale, 0, Math.Max(0, ImageHeight)));
    }

    /// <summary>Source-image rectangle currently visible; the bird's-eye view draws it.</summary>
    private void UpdateViewportRect()
    {
        if (_updatingViewport || ImageWidth <= 0 || ImageHeight <= 0 || ActualWidth <= 0)
        {
            return;
        }

        var content = GetContentRect();
        var scale = GetScale();
        if (scale <= 0 || content.Width <= 0)
        {
            return;
        }

        var left = Math.Clamp((0 - content.X) / scale, 0, ImageWidth);
        var top = Math.Clamp((0 - content.Y) / scale, 0, ImageHeight);
        var right = Math.Clamp((ActualWidth - content.X) / scale, 0, ImageWidth);
        var bottom = Math.Clamp((ActualHeight - content.Y) / scale, 0, ImageHeight);
        var viewport = new Rect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
        if (viewport.Width <= 0 || viewport.Height <= 0)
        {
            return;
        }

        _updatingViewport = true;
        try
        {
            SetCurrentValue(ViewportRectProperty, viewport);
        }
        finally
        {
            _updatingViewport = false;
        }
    }

    private static void DrawBoxNumber(
        DrawingContext context,
        Rect rect,
        int number,
        Color color,
        double pixelsPerDip)
    {
        var text = new FormattedText(
            number.ToString(CultureInfo.InvariantCulture),
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI Semibold"),
            11,
            Brushes.White,
            pixelsPerDip);
        var width = Math.Max(18, text.Width + 8);
        var badge = new Rect(rect.Left, rect.Top, width, 18);
        var brush = new SolidColorBrush(Color.FromArgb(0xF2, color.R, color.G, color.B));
        brush.Freeze();
        context.DrawRectangle(brush, null, badge);
        context.DrawText(text, new Point(badge.Left + ((badge.Width - text.Width) / 2), badge.Top + 2));
    }

    private static void DrawLabel(
        DrawingContext context,
        Rect rect,
        string label,
        Color color,
        double pixelsPerDip)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return;
        }

        var text = new FormattedText(
            label,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface("Microsoft YaHei UI"),
            12,
            Brushes.White,
            pixelsPerDip);
        var background = new SolidColorBrush(Color.FromArgb(0xE6, color.R, color.G, color.B));
        background.Freeze();
        var labelRect = new Rect(rect.Left, Math.Max(0, rect.Top - text.Height - 4), text.Width + 8, text.Height + 2);
        context.DrawRectangle(background, null, labelRect);
        context.DrawText(text, new Point(labelRect.Left + 4, labelRect.Top + 1));
    }

    private static Pen CreatePen(Color color, double thickness)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        var pen = new Pen(brush, thickness);
        pen.Freeze();
        return pen;
    }

    private static Color ParseColor(string value)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(value);
        }
        catch
        {
            return Color.FromRgb(0x3B, 0x82, 0xF6);
        }
    }
}
