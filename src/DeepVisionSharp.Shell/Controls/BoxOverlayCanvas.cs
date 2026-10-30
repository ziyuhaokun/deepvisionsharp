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
/// uses, so boxes stay glued to the pixels when the window resizes.
/// </summary>
public sealed class BoxOverlayCanvas : FrameworkElement
{
    private const double HandleSize = 9;
    private const double HitTolerance = 7;
    private const double MinimumDragPixels = 3;

    public static readonly DependencyProperty ImageWidthProperty = DependencyProperty.Register(
        nameof(ImageWidth), typeof(double), typeof(BoxOverlayCanvas),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ImageHeightProperty = DependencyProperty.Register(
        nameof(ImageHeight), typeof(double), typeof(BoxOverlayCanvas),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

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
    private Rect _originalBounds;
    private BoxShapeViewModel? _draftBox;

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
        if (content.Width <= 0 || content.Height <= 0)
        {
            return;
        }

        drawingContext.DrawRectangle(null, CreatePen(Color.FromArgb(0x66, 0x94, 0xA3, 0xB8), 1), content);

        if (Boxes is null)
        {
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        foreach (var item in Boxes.OfType<BoxShapeViewModel>())
        {
            var rect = ToScreenRect(item);
            var color = ParseColor(item.Color);
            var isSelected = ReferenceEquals(item, SelectedBox);
            var pen = CreatePen(color, isSelected ? 3 : 2);
            drawingContext.DrawRectangle(null, pen, rect);
            DrawLabel(drawingContext, rect, item.Label, color, dpi);

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

        if (SelectedBox is not null && !IsReadOnly)
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

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _dragMode = DragMode.None;
        _draftBox = null;
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

        var scale = Math.Min(ActualWidth / ImageWidth, ActualHeight / ImageHeight);
        var width = ImageWidth * scale;
        var height = ImageHeight * scale;
        return new Rect((ActualWidth - width) / 2, (ActualHeight - height) / 2, width, height);
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
