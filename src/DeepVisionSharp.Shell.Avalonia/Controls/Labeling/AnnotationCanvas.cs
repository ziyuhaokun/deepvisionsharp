using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using DeepVisionSharp.Presentation.ViewModels.Labeling;

namespace DeepVisionSharp.Shell.Avalonia.Controls.Labeling;

/// <summary>新建标注框的事件参数（图像坐标系）。</summary>
public sealed class AnnotationCreatedEventArgs(Rect box, BoxShapeViewModel createdBox) : EventArgs
{
    public Rect Box { get; } = box;

    public BoxShapeViewModel CreatedBox { get; } = createdBox;
}

/// <summary>标注框编辑完成的事件参数。</summary>
public sealed class AnnotationEditCompletedEventArgs(IReadOnlyList<Rect> snapshots) : EventArgs
{
    public IReadOnlyList<Rect> Snapshots { get; } = snapshots;
}

/// <summary>
/// 标注画布：把 Workspace 的图像按「适应窗口 × ZoomScale」画进控件，再叠上 <see cref="Boxes"/> 里的框。
/// 几何一律用真实的 <see cref="ImageWidth"/> / <see cref="ImageHeight"/> 图像坐标，屏幕坐标由
/// fit 比例 + 居中偏移 + 平移换算；不按 Bitmap 像素猜尺寸。
/// 编辑手势的撤销快照由 <see cref="AnnotationEditStarted"/> / <see cref="AnnotationEditCompleted"/> 成对发出：
/// 拖拽过程中只画预览，松手提交时才改 VM，Esc 取消不碰撤销栈。
/// </summary>
public sealed class AnnotationCanvas : Control
{
    private const double MinZoom = 0.05;
    private const double MaxZoom = 16;
    private const double HandleSize = 7;
    private const double HitTolerance = 7;
    private const double MinimumDragPixels = 3;
    private const double ZoomFactor = 1.12;

    private static readonly Cursor CrossCursor = new(StandardCursorType.Cross);
    private static readonly Cursor PanCursor = new(StandardCursorType.SizeAll);

    public static readonly StyledProperty<IBrush?> ActiveColorProperty =
        AvaloniaProperty.Register<AnnotationCanvas, IBrush?>(nameof(ActiveColor));

    public static readonly StyledProperty<string> ActiveLabelIdProperty =
        AvaloniaProperty.Register<AnnotationCanvas, string>(nameof(ActiveLabelId), string.Empty);

    public static readonly StyledProperty<string> ActiveLabelProperty =
        AvaloniaProperty.Register<AnnotationCanvas, string>(nameof(ActiveLabel), string.Empty);

    public static readonly StyledProperty<IBrush?> BackgroundProperty =
        Border.BackgroundProperty.AddOwner<AnnotationCanvas>();

    public static readonly StyledProperty<IReadOnlyList<BoxShapeViewModel>?> BoxesProperty =
        AvaloniaProperty.Register<AnnotationCanvas, IReadOnlyList<BoxShapeViewModel>?>(nameof(Boxes));

    public static readonly StyledProperty<IImage?> ImageSourceProperty =
        AvaloniaProperty.Register<AnnotationCanvas, IImage?>(nameof(ImageSource));

    public static readonly StyledProperty<bool> IsReadOnlyProperty =
        AvaloniaProperty.Register<AnnotationCanvas, bool>(nameof(IsReadOnly));

    public static readonly StyledProperty<double> ImageWidthProperty =
        AvaloniaProperty.Register<AnnotationCanvas, double>(nameof(ImageWidth));

    public static readonly StyledProperty<double> ImageHeightProperty =
        AvaloniaProperty.Register<AnnotationCanvas, double>(nameof(ImageHeight));

    public static readonly StyledProperty<ICommand?> RedoCommandProperty =
        AvaloniaProperty.Register<AnnotationCanvas, ICommand?>(nameof(RedoCommand));

    public static readonly StyledProperty<BoxShapeViewModel?> SelectedBoxProperty =
        AvaloniaProperty.Register<AnnotationCanvas, BoxShapeViewModel?>(
            nameof(SelectedBox), default, defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<bool> ShowLabelNamesProperty =
        AvaloniaProperty.Register<AnnotationCanvas, bool>(nameof(ShowLabelNames), true);

    public static readonly StyledProperty<BoxToolMode> ToolModeProperty =
        AvaloniaProperty.Register<AnnotationCanvas, BoxToolMode>(nameof(ToolMode));

    public static readonly StyledProperty<ICommand?> UndoCommandProperty =
        AvaloniaProperty.Register<AnnotationCanvas, ICommand?>(nameof(UndoCommand));

    public static readonly StyledProperty<bool> UseTransparentFillProperty =
        AvaloniaProperty.Register<AnnotationCanvas, bool>(nameof(UseTransparentFill));

    public static readonly StyledProperty<Rect> ViewportRectProperty =
        AvaloniaProperty.Register<AnnotationCanvas, Rect>(
            nameof(ViewportRect), default, defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<double> ZoomScaleProperty =
        AvaloniaProperty.Register<AnnotationCanvas, double>(nameof(ZoomScale), 1d);

    private readonly List<BoxShapeViewModel> _trackedBoxes = new();

    private Vector _pan;
    private Point _dragStartScreen;
    private bool _updatingViewport;
    private DragMode _dragMode;
    private HandleKind _handle;
    private Point _dragStartImage;
    private Rect _originalBounds;
    private Rect? _previewBounds;
    private BoxShapeViewModel? _dragTarget;
    private bool _isPanning;

    static AnnotationCanvas()
    {
        AffectsRender<AnnotationCanvas>(
            ActiveColorProperty,
            ActiveLabelIdProperty,
            ActiveLabelProperty,
            BackgroundProperty,
            BoxesProperty,
            ImageSourceProperty,
            ImageWidthProperty,
            ImageHeightProperty,
            SelectedBoxProperty,
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

    /// <summary>视口变化（缩放或平移）后触发，页面据此同步鸟瞰图。</summary>
    public event EventHandler? ViewportChanged;

    /// <summary>即将真正改动框集合/几何：页面在这里调用 Editor.BeginBoxEditCommand。</summary>
    public event EventHandler? AnnotationEditStarted;

    /// <summary>新框已经加入集合：页面在这里调用 Editor.SelectBoxCommand。</summary>
    public event EventHandler<AnnotationCreatedEventArgs>? AnnotationCreated;

    /// <summary>框集合/几何已经改完：页面在这里调用 Editor.NotifyBoxesEditedCommand。</summary>
    public event EventHandler<AnnotationEditCompletedEventArgs>? AnnotationEditCompleted;

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

    public IBrush? Background
    {
        get => GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
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

    /// <summary>图像坐标系的真实宽度；所有命中测试与框几何都以它为准。</summary>
    public double ImageWidth
    {
        get => GetValue(ImageWidthProperty);
        set => SetValue(ImageWidthProperty, value);
    }

    /// <summary>图像坐标系的真实高度。</summary>
    public double ImageHeight
    {
        get => GetValue(ImageHeightProperty);
        set => SetValue(ImageHeightProperty, value);
    }

    public bool IsReadOnly
    {
        get => GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    public ICommand? RedoCommand
    {
        get => GetValue(RedoCommandProperty);
        set => SetValue(RedoCommandProperty, value);
    }

    public BoxShapeViewModel? SelectedBox
    {
        get => GetValue(SelectedBoxProperty);
        set => SetValue(SelectedBoxProperty, value);
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

    public ICommand? UndoCommand
    {
        get => GetValue(UndoCommandProperty);
        set => SetValue(UndoCommandProperty, value);
    }

    public bool UseTransparentFill
    {
        get => GetValue(UseTransparentFillProperty);
        set => SetValue(UseTransparentFillProperty, value);
    }

    /// <summary>当前可见区域的图像坐标矩形，供 VM / 鸟瞰图同步。</summary>
    public Rect ViewportRect
    {
        get => GetValue(ViewportRectProperty);
        set => SetValue(ViewportRectProperty, value);
    }

    /// <summary>在「适应窗口」基础上的缩放倍数；1 = 正好铺满可用区域。</summary>
    public double ZoomScale
    {
        get => GetValue(ZoomScaleProperty);
        set => SetValue(ZoomScaleProperty, ClampZoom(value));
    }

    private Size ImageSize => new(Math.Max(0, ImageWidth), Math.Max(0, ImageHeight));

    public void ZoomIn() => ZoomScale *= 1.25;

    public void ZoomOut() => ZoomScale /= 1.25;

    /// <summary>缩放回适应窗口并居中。</summary>
    public void FitToView()
    {
        _pan = default;
        SetCurrentValue(ZoomScaleProperty, 1d);
        RaiseViewChanged();
    }

    /// <summary>把图像坐标里的一点移到视口中心（鸟瞰图导航用）。</summary>
    public void CenterViewportAt(Point imagePoint)
    {
        var image = ImageSize;
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

        RaiseViewChanged();
    }

    /// <summary>当前可见区域的图像坐标矩形，鸟瞰图画视口框用。</summary>
    public Rect GetViewportImageRect()
    {
        var image = ImageSize;
        var scale = EffectiveScale(image);
        if (scale <= 0)
        {
            return new Rect(0, 0, image.Width, image.Height);
        }

        var drawn = ContentRect(image, scale);
        var left = Math.Clamp((0 - drawn.Left) / scale, 0, image.Width);
        var top = Math.Clamp((0 - drawn.Top) / scale, 0, image.Height);
        var right = Math.Clamp((Bounds.Width - drawn.Left) / scale, 0, image.Width);
        var bottom = Math.Clamp((Bounds.Height - drawn.Top) / scale, 0, image.Height);
        return new Rect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    /// <summary>取消正在进行的绘制/移动/缩放，不产生任何 VM 变更，撤销栈保持不变。</summary>
    public bool CancelGeometryEdit()
    {
        if (_dragMode == DragMode.None && !_isPanning)
        {
            return false;
        }

        ClearDragState();
        _isPanning = false;
        ResetCursor();
        InvalidateVisual();
        return true;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == BoxesProperty)
        {
            TrackBoxes(change.GetOldValue<IReadOnlyList<BoxShapeViewModel>?>(),
                change.GetNewValue<IReadOnlyList<BoxShapeViewModel>?>());
        }
        else if (change.Property == ZoomScaleProperty)
        {
            RaiseViewChanged();
        }
        else if (change.Property == ImageWidthProperty || change.Property == ImageHeightProperty)
        {
            if (ImageWidth > 0 && ImageHeight > 0)
            {
                _pan = default;
                ZoomScale = 1;
                RaiseViewChanged();
            }
        }
        else if (change.Property == SelectedBoxProperty)
        {
            InvalidateVisual();
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (!HasImage)
        {
            return;
        }

        var buttons = e.GetCurrentPoint(this).Properties;
        if (buttons.PointerUpdateKind == PointerUpdateKind.RightButtonPressed)
        {
            _isPanning = true;
            _dragStartScreen = e.GetPosition(this);
            e.Pointer.Capture(this);
            Cursor = PanCursor;
            e.Handled = true;
            return;
        }

        if (!buttons.IsLeftButtonPressed)
        {
            return;
        }

        if (e.Pointer.Type == PointerType.Mouse && e.ClickCount == 2)
        {
            FitToView();
            e.Handled = true;
            return;
        }

        if (IsReadOnly)
        {
            return;
        }

        Focus();
        var screenPoint = e.GetPosition(this);
        var imagePoint = ToImagePoint(screenPoint);

        if (SelectedBox is not null)
        {
            var handle = HitTestHandle(SelectedBox, screenPoint);
            if (handle != HandleKind.None)
            {
                _dragMode = DragMode.Resize;
                _handle = handle;
                _dragTarget = SelectedBox;
                _dragStartImage = imagePoint;
                _originalBounds = BoundsOf(SelectedBox);
                _previewBounds = _originalBounds;
                e.Pointer.Capture(this);
                e.Handled = true;
                return;
            }
        }

        var hit = HitTestBox(imagePoint);
        if (hit is not null)
        {
            SelectedBox = hit;
            if (ToolMode == BoxToolMode.Select)
            {
                _dragMode = DragMode.Move;
                _handle = HandleKind.None;
                _dragTarget = hit;
                _dragStartImage = imagePoint;
                _originalBounds = BoundsOf(hit);
                _previewBounds = _originalBounds;
                e.Pointer.Capture(this);
            }

            e.Handled = true;
            return;
        }

        if (ToolMode != BoxToolMode.Draw || string.IsNullOrWhiteSpace(ActiveLabel))
        {
            SelectedBox = null;
            return;
        }

        SelectedBox = null;
        _dragMode = DragMode.Draw;
        _handle = HandleKind.None;
        _dragTarget = null;
        _dragStartImage = imagePoint;
        _previewBounds = new Rect(imagePoint.X, imagePoint.Y, 1, 1);
        Cursor = CrossCursor;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_isPanning)
        {
            // Avalonia reports a button release as a move while another mouse button is held.
            if (!e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
            {
                EndPan(e.Pointer);
                e.Handled = true;
                return;
            }

            var current = e.GetPosition(this);
            _pan += current - _dragStartScreen;
            _dragStartScreen = current;
            RaiseViewChanged();
            e.Handled = true;
            return;
        }

        if (_dragMode == DragMode.None)
        {
            Cursor = ToolMode == BoxToolMode.Draw && !IsReadOnly && HasImage ? CrossCursor : null;
            return;
        }

        var imagePoint = ToImagePoint(e.GetPosition(this));
        switch (_dragMode)
        {
            case DragMode.Draw:
                var left = Math.Clamp(Math.Min(_dragStartImage.X, imagePoint.X), 0, ImageWidth);
                var top = Math.Clamp(Math.Min(_dragStartImage.Y, imagePoint.Y), 0, ImageHeight);
                var right = Math.Clamp(Math.Max(_dragStartImage.X, imagePoint.X), 0, ImageWidth);
                var bottom = Math.Clamp(Math.Max(_dragStartImage.Y, imagePoint.Y), 0, ImageHeight);
                _previewBounds = new Rect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
                break;
            case DragMode.Move:
                var deltaX = imagePoint.X - _dragStartImage.X;
                var deltaY = imagePoint.Y - _dragStartImage.Y;
                var x = Math.Clamp(_originalBounds.X + deltaX, 0, Math.Max(0, ImageWidth - _originalBounds.Width));
                var y = Math.Clamp(_originalBounds.Y + deltaY, 0, Math.Max(0, ImageHeight - _originalBounds.Height));
                _previewBounds = new Rect(x, y, _originalBounds.Width, _originalBounds.Height);
                break;
            case DragMode.Resize:
                _previewBounds = ResizePreview(imagePoint);
                break;
        }

        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_isPanning && e.InitialPressMouseButton == MouseButton.Right)
        {
            EndPan(e.Pointer);
            e.Handled = true;
            return;
        }

        if (_dragMode == DragMode.None || e.InitialPressMouseButton != MouseButton.Left)
        {
            return;
        }

        CommitDrag();
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        CancelGeometryEdit();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        if (IsReadOnly || !HasImage)
        {
            return;
        }

        var factor = e.Delta.Y > 0 ? ZoomFactor : 1 / ZoomFactor;
        ZoomAround(e.GetPosition(this), factor);
        e.Handled = true;
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
            DeleteSelectedBoxCore();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && CancelGeometryEdit())
        {
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.Z)
        {
            ExecuteCommand(UndoCommand);
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.Y)
        {
            ExecuteCommand(RedoCommand);
            e.Handled = true;
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        context.FillRectangle(Background ?? Brushes.Transparent, new Rect(Bounds.Size));

        var image = ImageSize;
        var scale = EffectiveScale(image);
        var drawn = ContentRect(image, scale);

        if (ImageSource is { } source)
        {
            context.DrawImage(source, drawn);
        }

        DrawBoxes(context, scale, drawn);
        DrawDragPreview(context, scale, drawn);
    }

    private bool HasImage => ImageWidth > 0 && ImageHeight > 0;

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
            if ((_dragMode is DragMode.Move or DragMode.Resize) && ReferenceEquals(box, _dragTarget))
            {
                continue;
            }

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

    private void DrawDragPreview(DrawingContext context, double scale, Rect drawn)
    {
        if (_previewBounds is not { } preview)
        {
            return;
        }

        var screenRect = new Rect(
            drawn.Left + (preview.X * scale),
            drawn.Top + (preview.Y * scale),
            preview.Width * scale,
            preview.Height * scale);

        if (_dragMode == DragMode.Draw)
        {
            var color = AnnotationRenderer.ParseColor(
                new BoxShapeViewModel(ActiveLabel, 0, 0, 1, 1).Color);
            AnnotationRenderer.DrawPreviewBox(context, screenRect, color);
            return;
        }

        var boxColor = AnnotationRenderer.ParseColor(_dragTarget?.Color);
        AnnotationRenderer.DrawCanvasAnnotation(
            context,
            screenRect,
            boxColor,
            selected: true,
            primary: true,
            showLabel: ShowLabelNames,
            label: _dragTarget?.Label ?? ActiveLabel,
            handleSize: HandleSize,
            transparentFill: UseTransparentFill);
    }

    private void CommitDrag()
    {
        var mode = _dragMode;
        var preview = _previewBounds;
        var target = _dragTarget;
        var original = _originalBounds;
        ClearDragState();
        ResetCursor();

        if (mode == DragMode.Draw)
        {
            if (preview is null || preview.Value.Width <= 0 || preview.Value.Height <= 0
                || !IsLargeEnough(preview.Value))
            {
                InvalidateVisual();
                return;
            }

            var created = new BoxShapeViewModel(
                ActiveLabel, preview.Value.X, preview.Value.Y, preview.Value.Width, preview.Value.Height);
            RaiseEditStarted();
            if (Boxes is IList<BoxShapeViewModel> list)
            {
                list.Add(created);
            }

            SelectedBox = created;
            AnnotationCreated?.Invoke(this, new AnnotationCreatedEventArgs(preview.Value, created));
            RaiseEditCompleted(preview.Value);
            return;
        }

        if (target is null || preview is null || preview.Value.Equals(original))
        {
            InvalidateVisual();
            return;
        }

        RaiseEditStarted();
        target.SetBounds(preview.Value.X, preview.Value.Y, preview.Value.Width, preview.Value.Height);
        RaiseEditCompleted(preview.Value);
    }

    private void DeleteSelectedBoxCore()
    {
        var box = SelectedBox;
        if (box is null || Boxes is not IList<BoxShapeViewModel> list)
        {
            return;
        }

        var bounds = BoundsOf(box);
        RaiseEditStarted();
        list.Remove(box);
        SelectedBox = null;
        RaiseEditCompleted(bounds);
    }

    private void RaiseEditStarted() =>
        AnnotationEditStarted?.Invoke(this, EventArgs.Empty);

    private void RaiseEditCompleted(Rect changed) =>
        AnnotationEditCompleted?.Invoke(this, new AnnotationEditCompletedEventArgs([changed]));

    private void ClearDragState()
    {
        _dragMode = DragMode.None;
        _handle = HandleKind.None;
        _dragStartImage = default;
        _originalBounds = default;
        _previewBounds = null;
        _dragTarget = null;
    }

    private void EndPan(IPointer pointer)
    {
        _isPanning = false;
        ResetCursor();
        pointer.Capture(null);
    }

    private void ResetCursor() => Cursor = null;

    private bool IsLargeEnough(Rect imageRect)
    {
        var scale = EffectiveScale(ImageSize);
        return imageRect.Width * scale >= MinimumDragPixels && imageRect.Height * scale >= MinimumDragPixels;
    }

    private Rect ResizePreview(Point imagePoint)
    {
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
        return new Rect(x, y, width, height);
    }

    private static Rect BoundsOf(BoxShapeViewModel box) => new(box.X, box.Y, box.Width, box.Height);

    private BoxShapeViewModel? HitTestBox(Point imagePoint)
    {
        if (Boxes is not { Count: > 0 } boxes)
        {
            return null;
        }

        for (var index = boxes.Count - 1; index >= 0; index--)
        {
            var box = boxes[index];
            if (imagePoint.X >= box.X && imagePoint.X <= box.X + box.Width
                && imagePoint.Y >= box.Y && imagePoint.Y <= box.Y + box.Height)
            {
                return box;
            }
        }

        return null;
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

    private Rect ToScreenRect(BoxShapeViewModel box)
    {
        var image = ImageSize;
        var drawn = ContentRect(image, EffectiveScale(image));
        var scale = EffectiveScale(image);
        return new Rect(
            drawn.Left + (box.X * scale),
            drawn.Top + (box.Y * scale),
            Math.Max(1, box.Width * scale),
            Math.Max(1, box.Height * scale));
    }

    private Point ToImagePoint(Point screenPoint)
    {
        var image = ImageSize;
        var scale = EffectiveScale(image);
        if (scale <= 0)
        {
            return new Point(0, 0);
        }

        var drawn = ContentRect(image, scale);
        return new Point(
            Math.Clamp((screenPoint.X - drawn.Left) / scale, 0, image.Width),
            Math.Clamp((screenPoint.Y - drawn.Top) / scale, 0, image.Height));
    }

    private void ZoomAround(Point anchor, double factor)
    {
        var before = ToImagePoint(anchor);
        var target = ClampZoom(ZoomScale * factor);
        if (Math.Abs(target - ZoomScale) < 0.0001)
        {
            return;
        }

        ZoomScale = target;
        var image = ImageSize;
        var scale = EffectiveScale(image);
        var drawn = ContentRect(image, scale);
        var current = new Point(drawn.Left + (before.X * scale), drawn.Top + (before.Y * scale));
        _pan += anchor - current;
        RaiseViewChanged();
    }

    private void RaiseViewChanged()
    {
        UpdateViewportRect();
        InvalidateVisual();
        RaiseViewportChanged();
    }

    private void UpdateViewportRect()
    {
        if (_updatingViewport || !HasImage || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        var viewport = GetViewportImageRect();
        if (viewport.Width <= 0 || viewport.Height <= 0 || ViewportRect == viewport)
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

    private static double ClampZoom(double value) =>
        double.IsFinite(value) ? Math.Clamp(value, MinZoom, MaxZoom) : 1d;

    private static void ExecuteCommand(ICommand? command)
    {
        if (command?.CanExecute(null) == true)
        {
            command.Execute(null);
        }
    }

    private void RaiseViewportChanged() => ViewportChanged?.Invoke(this, EventArgs.Empty);
}
