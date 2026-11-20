using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace DeepVisionSharp.Shell.Avalonia.Controls.Inference;

/// <summary>
/// 画布右上角的悬停工具条：撤销/重做、缩小/缩放比例/放大/适应窗口，可置顶保持展开。
/// 从 deepvision 的 CanvasViewToolbar 移植；缩放数学归页面所有，置顶状态只在本实例内保留。
/// </summary>
public partial class CanvasViewToolbar : UserControl
{
    private const int HideDelayMilliseconds = 140;
    private const double HitZoneSize = 56;

    public static readonly StyledProperty<string> ZoomTextProperty =
        AvaloniaProperty.Register<CanvasViewToolbar, string>(nameof(ZoomText), "100%");

    public static readonly StyledProperty<bool> CanUndoProperty =
        AvaloniaProperty.Register<CanvasViewToolbar, bool>(nameof(CanUndo));

    public static readonly StyledProperty<bool> CanRedoProperty =
        AvaloniaProperty.Register<CanvasViewToolbar, bool>(nameof(CanRedo));

    /// <summary>与 deepvision 的页面属性对齐；本版置顶不落盘，不做跨页面记忆。</summary>
    public static readonly StyledProperty<string> PinIdProperty =
        AvaloniaProperty.Register<CanvasViewToolbar, string>(nameof(PinId), string.Empty);

    public static readonly StyledProperty<object?> BodyProperty =
        AvaloniaProperty.Register<CanvasViewToolbar, object?>(nameof(Body));

    public static readonly StyledProperty<IDataTemplate?> BodyTemplateProperty =
        AvaloniaProperty.Register<CanvasViewToolbar, IDataTemplate?>(nameof(BodyTemplate));

    public static readonly StyledProperty<CanvasCornerFloatPlacement> PlacementProperty =
        AvaloniaProperty.Register<CanvasViewToolbar, CanvasCornerFloatPlacement>(
            nameof(Placement), CanvasCornerFloatPlacement.TopRight);

    public static readonly StyledProperty<bool> UseOpaqueBackgroundProperty =
        AvaloniaProperty.Register<CanvasViewToolbar, bool>(nameof(UseOpaqueBackground));

    private bool _isHovered;
    private bool _isPinned;
    private DispatcherTimer? _hideTimer;

    private Border? _hitZone;
    private Border? _floatPanel;
    private StackPanel? _floatHost;
    private Button? _pinButton;
    private ContentPresenter? _bodyHost;

    static CanvasViewToolbar()
    {
        PlacementProperty.Changed.AddClassHandler<CanvasViewToolbar>((control, _) => control.ApplyPlacement());
        BodyProperty.Changed.AddClassHandler<CanvasViewToolbar>((control, _) => control.ApplyBody());
        BodyTemplateProperty.Changed.AddClassHandler<CanvasViewToolbar>((control, _) => control.ApplyBody());
        UseOpaqueBackgroundProperty.Changed.AddClassHandler<CanvasViewToolbar>(
            (control, _) => control.ApplyPanelBackground());
    }

    public CanvasViewToolbar()
    {
        InitializeComponent();
        _hitZone = this.FindControl<Border>("HitZone");
        _floatPanel = this.FindControl<Border>("FloatPanel");
        _floatHost = this.FindControl<StackPanel>("FloatHost");
        _pinButton = this.FindControl<Button>("PinButton");
        _bodyHost = this.FindControl<ContentPresenter>("BodyHost");

        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
        ApplyPlacement();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public string ZoomText
    {
        get => GetValue(ZoomTextProperty);
        set => SetValue(ZoomTextProperty, value);
    }

    public bool CanUndo
    {
        get => GetValue(CanUndoProperty);
        set => SetValue(CanUndoProperty, value);
    }

    public bool CanRedo
    {
        get => GetValue(CanRedoProperty);
        set => SetValue(CanRedoProperty, value);
    }

    public string PinId
    {
        get => GetValue(PinIdProperty);
        set => SetValue(PinIdProperty, value);
    }

    public object? Body
    {
        get => GetValue(BodyProperty);
        set => SetValue(BodyProperty, value);
    }

    public IDataTemplate? BodyTemplate
    {
        get => GetValue(BodyTemplateProperty);
        set => SetValue(BodyTemplateProperty, value);
    }

    public CanvasCornerFloatPlacement Placement
    {
        get => GetValue(PlacementProperty);
        set => SetValue(PlacementProperty, value);
    }

    public bool UseOpaqueBackground
    {
        get => GetValue(UseOpaqueBackgroundProperty);
        set => SetValue(UseOpaqueBackgroundProperty, value);
    }

    public event EventHandler<RoutedEventArgs>? UndoClick;

    public event EventHandler<RoutedEventArgs>? RedoClick;

    public event EventHandler<RoutedEventArgs>? ZoomInClick;

    public event EventHandler<RoutedEventArgs>? ZoomOutClick;

    public event EventHandler<RoutedEventArgs>? FitClick;

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        ApplyBody();
        ApplyPlacement();
        ApplyPanelBackground();
        SyncVisibility();

        if (_hitZone is not null)
        {
            _hitZone.PointerEntered += OnPointerEnter;
            _hitZone.PointerExited += OnPointerLeave;
        }

        if (_floatPanel is not null)
        {
            _floatPanel.PointerEntered += OnPointerEnter;
            _floatPanel.PointerExited += OnPointerLeave;
        }
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        if (_hitZone is not null)
        {
            _hitZone.PointerEntered -= OnPointerEnter;
            _hitZone.PointerExited -= OnPointerLeave;
        }

        if (_floatPanel is not null)
        {
            _floatPanel.PointerEntered -= OnPointerEnter;
            _floatPanel.PointerExited -= OnPointerLeave;
        }

        _hideTimer?.Stop();
        _hideTimer = null;
    }

    private void ApplyBody()
    {
        if (_bodyHost is null)
        {
            return;
        }

        _bodyHost.Content = Body;
        _bodyHost.ContentTemplate = BodyTemplate;
        _bodyHost.IsVisible = Body is not null;
    }

    private void ApplyPanelBackground()
    {
        if (_floatPanel is null)
        {
            return;
        }

        _floatPanel.Background = UseOpaqueBackground
            ? LookupBrush("IvSurface2Brush") ?? LookupBrush("IvBackgroundBrush") ?? new SolidColorBrush(Color.FromRgb(32, 32, 32))
            : LookupBrush("IvSurface3Brush") ?? new SolidColorBrush(Color.FromArgb(0xCC, 32, 32, 32));
    }

    private IBrush? LookupBrush(string resourceKey)
        => this.TryFindResource(resourceKey, out var value) ? value as IBrush : null;

    private void ApplyPlacement()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;

        HorizontalAlignment alignmentX;
        VerticalAlignment alignmentY;

        switch (Placement)
        {
            case CanvasCornerFloatPlacement.TopLeft:
                alignmentX = HorizontalAlignment.Left;
                alignmentY = VerticalAlignment.Top;
                break;
            case CanvasCornerFloatPlacement.BottomLeft:
                alignmentX = HorizontalAlignment.Left;
                alignmentY = VerticalAlignment.Bottom;
                break;
            default:
                alignmentX = HorizontalAlignment.Right;
                alignmentY = VerticalAlignment.Top;
                break;
        }

        if (_floatHost is not null)
        {
            _floatHost.Orientation = Orientation.Horizontal;
        }

        if (_hitZone is not null)
        {
            _hitZone.Width = HitZoneSize;
            _hitZone.Height = HitZoneSize;
            _hitZone.HorizontalAlignment = alignmentX;
            _hitZone.VerticalAlignment = alignmentY;
        }

        if (_floatPanel is not null)
        {
            _floatPanel.HorizontalAlignment = alignmentX;
            _floatPanel.VerticalAlignment = alignmentY;
            _floatPanel.Margin = new Thickness(12);
            _floatPanel.Padding = new Thickness(2, 2, 3, 2);
        }

        if (_pinButton is not null)
        {
            _pinButton.Margin = new Thickness(0);
        }
    }

    private bool IsFloatOpen() => _isPinned || _isHovered;

    private void OnPointerEnter(object? sender, PointerEventArgs e) => OpenFloat();

    private void OnPointerLeave(object? sender, PointerEventArgs e) => ScheduleClose();

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

    private void OnPinButtonClicked(object? sender, RoutedEventArgs e)
    {
        _isPinned = !_isPinned;
        SyncVisibility();
    }

    private void SyncVisibility()
    {
        var show = IsFloatOpen();
        if (_floatPanel is not null)
        {
            _floatPanel.IsVisible = show;
            _floatPanel.Opacity = show ? 1 : 0;
            _floatPanel.IsHitTestVisible = show;
        }

        if (_pinButton is not null)
        {
            ToolTip.SetTip(_pinButton, _isPinned ? "取消置顶" : "置顶（保持显示）");
            _pinButton.Classes.Set("pinned", _isPinned);
        }
    }

    private void OnUndoClick(object? sender, RoutedEventArgs e) => UndoClick?.Invoke(this, new RoutedEventArgs());

    private void OnRedoClick(object? sender, RoutedEventArgs e) => RedoClick?.Invoke(this, new RoutedEventArgs());

    private void OnZoomInClick(object? sender, RoutedEventArgs e) => ZoomInClick?.Invoke(this, new RoutedEventArgs());

    private void OnZoomOutClick(object? sender, RoutedEventArgs e) => ZoomOutClick?.Invoke(this, new RoutedEventArgs());

    private void OnFitClick(object? sender, RoutedEventArgs e) => FitClick?.Invoke(this, new RoutedEventArgs());
}
