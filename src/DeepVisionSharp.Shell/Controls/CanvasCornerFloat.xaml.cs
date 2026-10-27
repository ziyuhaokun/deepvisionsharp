using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DeepVisionSharp.Shell.Services;

namespace DeepVisionSharp.Shell.Controls;

public enum CanvasCornerFloatPlacement
{
    TopLeft,
    TopRight,
    BottomLeft
}

/// <summary>
/// Canvas corner toolbar: hidden by default, revealed on corner hover, optionally pinned.
/// Mirrors preview/js/03-labeling.js initLbFloatPins().
/// </summary>
public partial class CanvasCornerFloat : UserControl
{
    private const int HideDelayMilliseconds = 140;
    private const double HitZoneSize = 56;

    public static readonly DependencyProperty PlacementProperty = DependencyProperty.Register(
        nameof(Placement),
        typeof(CanvasCornerFloatPlacement),
        typeof(CanvasCornerFloat),
        new PropertyMetadata(CanvasCornerFloatPlacement.TopLeft, OnPlacementChanged));

    public static readonly DependencyProperty PinIdProperty = DependencyProperty.Register(
        nameof(PinId),
        typeof(string),
        typeof(CanvasCornerFloat),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty BodyProperty = DependencyProperty.Register(
        nameof(Body),
        typeof(object),
        typeof(CanvasCornerFloat),
        new PropertyMetadata(null, OnBodyChanged));

    public static readonly DependencyProperty BodyTemplateProperty = DependencyProperty.Register(
        nameof(BodyTemplate),
        typeof(DataTemplate),
        typeof(CanvasCornerFloat),
        new PropertyMetadata(null, OnBodyChanged));

    /// <summary>
    /// When true, panel uses an opaque solid surface (readable result cards). Default keeps theme secondary fill (often translucent).
    /// </summary>
    public static readonly DependencyProperty UseOpaqueBackgroundProperty = DependencyProperty.Register(
        nameof(UseOpaqueBackground),
        typeof(bool),
        typeof(CanvasCornerFloat),
        new PropertyMetadata(false, OnUseOpaqueBackgroundChanged));

    private bool _isHovered;
    private bool _isPinned;
    private DispatcherTimer? _hideTimer;

    public CanvasCornerFloat()
    {
        InitializeComponent();
        ApplyPlacement();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public CanvasCornerFloatPlacement Placement
    {
        get => (CanvasCornerFloatPlacement)GetValue(PlacementProperty);
        set => SetValue(PlacementProperty, value);
    }

    public string PinId
    {
        get => (string)GetValue(PinIdProperty);
        set => SetValue(PinIdProperty, value);
    }

    public object? Body
    {
        get => GetValue(BodyProperty);
        set => SetValue(BodyProperty, value);
    }

    public DataTemplate? BodyTemplate
    {
        get => (DataTemplate?)GetValue(BodyTemplateProperty);
        set => SetValue(BodyTemplateProperty, value);
    }

    public bool UseOpaqueBackground
    {
        get => (bool)GetValue(UseOpaqueBackgroundProperty);
        set => SetValue(UseOpaqueBackgroundProperty, value);
    }

    protected override HitTestResult HitTestCore(PointHitTestParameters hitTestParameters)
    {
        if (!IsVisible)
        {
            return null!;
        }

        var point = hitTestParameters.HitPoint;
        if (IsInsideHitZone(point) || (IsFloatOpen() && IsInsideFloatPanel(point)))
        {
            return base.HitTestCore(hitTestParameters);
        }

        return null!;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        BodyHost.Content = Body;
        BodyHost.ContentTemplate = BodyTemplate;
        ApplyPlacement();
        ApplyPanelBackground();
        _isPinned = !string.IsNullOrWhiteSpace(PinId) && LabelingCanvasFloatPreferences.GetPin(PinId);
        SyncVisibility();

        HitZone.MouseEnter += OnPointerEnter;
        HitZone.MouseLeave += OnPointerLeave;
        FloatPanel.MouseEnter += OnPointerEnter;
        FloatPanel.MouseLeave += OnPointerLeave;
    }

    private static void OnUseOpaqueBackgroundChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CanvasCornerFloat control)
        {
            control.ApplyPanelBackground();
        }
    }

    private void ApplyPanelBackground()
    {
        if (FloatPanel is null)
        {
            return;
        }

        if (UseOpaqueBackground)
        {
            // Prefer fully opaque theme surfaces so result text is not washed out by canvas below.
            FloatPanel.Background =
                TryFindResource("SolidBackgroundFillColorBaseBrush") as Brush
                ?? TryFindResource("ApplicationBackgroundBrush") as Brush
                ?? TryFindResource("CardBackgroundFillColorDefaultBrush") as Brush
                ?? new SolidColorBrush(Color.FromRgb(32, 32, 32));
        }
        else
        {
            FloatPanel.Background =
                TryFindResource("ControlFillColorSecondaryBrush") as Brush
                ?? new SolidColorBrush(Color.FromArgb(0xCC, 32, 32, 32));
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        HitZone.MouseEnter -= OnPointerEnter;
        HitZone.MouseLeave -= OnPointerLeave;
        FloatPanel.MouseEnter -= OnPointerEnter;
        FloatPanel.MouseLeave -= OnPointerLeave;
        _hideTimer?.Stop();
        _hideTimer = null;
    }

    private static void OnPlacementChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CanvasCornerFloat control)
        {
            control.ApplyPlacement();
        }
    }

    private static void OnBodyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CanvasCornerFloat control && control.BodyHost is not null)
        {
            control.BodyHost.Content = control.Body;
            control.BodyHost.ContentTemplate = control.BodyTemplate;
        }
    }

    private void ApplyPlacement()
    {
        HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
        VerticalAlignment = System.Windows.VerticalAlignment.Stretch;

        HorizontalAlignment alignmentX;
        VerticalAlignment alignmentY;

        switch (Placement)
        {
            case CanvasCornerFloatPlacement.TopRight:
                alignmentX = HorizontalAlignment.Right;
                alignmentY = VerticalAlignment.Top;
                FloatHost.Orientation = Orientation.Horizontal;
                break;
            case CanvasCornerFloatPlacement.BottomLeft:
                alignmentX = HorizontalAlignment.Left;
                alignmentY = VerticalAlignment.Bottom;
                FloatHost.Orientation = Orientation.Horizontal;
                break;
            default:
                alignmentX = HorizontalAlignment.Left;
                alignmentY = VerticalAlignment.Top;
                FloatHost.Orientation = Orientation.Horizontal;
                break;
        }

        HitZone.Width = HitZoneSize;
        HitZone.Height = HitZoneSize;
        HitZone.HorizontalAlignment = alignmentX;
        HitZone.VerticalAlignment = alignmentY;

        FloatPanel.HorizontalAlignment = alignmentX;
        FloatPanel.VerticalAlignment = alignmentY;
        FloatPanel.Margin = new Thickness(12);
        FloatPanel.Padding = new Thickness(6, 8, 8, 8);
        PinButton.Margin = new Thickness(0, 0, 4, 0);
    }

    private bool IsFloatOpen() => _isPinned || _isHovered;

    private bool IsInsideHitZone(Point point) => GetElementBounds(HitZone).Contains(point);

    private bool IsInsideFloatPanel(Point point)
    {
        if (FloatPanel.Visibility != Visibility.Visible)
        {
            return false;
        }

        return GetElementBounds(FloatPanel).Contains(point);
    }

    private Rect GetElementBounds(FrameworkElement element)
    {
        var width = element.ActualWidth > 0 ? element.ActualWidth : element.Width;
        var height = element.ActualHeight > 0 ? element.ActualHeight : element.Height;
        if (width <= 0 || height <= 0)
        {
            return Rect.Empty;
        }

        var topLeft = element.TranslatePoint(new Point(0, 0), this);
        return new Rect(topLeft, new Size(width, height));
    }

    private void OnPointerEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        OpenFloat();
    }

    private void OnPointerLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        ScheduleClose();
    }

    private void OpenFloat()
    {
        _hideTimer?.Stop();
        _isHovered = true;
        SyncVisibility();
    }

    private void ScheduleClose()
    {
        _hideTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(HideDelayMilliseconds) };
        _hideTimer.Stop();
        _hideTimer.Tick -= OnHideTimerTick;
        _hideTimer.Tick += OnHideTimerTick;
        _hideTimer.Start();
    }

    private void OnHideTimerTick(object? sender, EventArgs e)
    {
        _hideTimer?.Stop();
        _isHovered = false;
        SyncVisibility();
    }

    private void OnPinButtonClicked(object sender, RoutedEventArgs e)
    {
        _isPinned = !_isPinned;
        if (!string.IsNullOrWhiteSpace(PinId))
        {
            LabelingCanvasFloatPreferences.SetPin(PinId, _isPinned);
        }

        SyncVisibility();
    }

    private void SyncVisibility()
    {
        var show = IsFloatOpen();
        FloatPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        FloatPanel.Opacity = show ? 1 : 0;
        FloatPanel.IsHitTestVisible = show;

        PinButton.Tag = _isPinned;
        PinButton.ToolTip = _isPinned ? "取消置顶" : "置顶（保持显示）";

        var offVisibility = _isPinned ? Visibility.Collapsed : Visibility.Visible;
        var onVisibility = _isPinned ? Visibility.Visible : Visibility.Collapsed;
        PinOffMain.Visibility = offVisibility;
        PinOffDetail.Visibility = offVisibility;
        PinOnHead.Visibility = onVisibility;
        PinOnStem.Visibility = onVisibility;
    }
}
