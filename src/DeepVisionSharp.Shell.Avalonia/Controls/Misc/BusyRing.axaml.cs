using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace DeepVisionSharp.Shell.Avalonia.Controls;

/// <summary>忙碌环：绕中心匀速旋转，由 UI 线程计时器驱动（Avalonia 没有 RenderTransform 动画器）。</summary>
public partial class BusyRing : UserControl
{
    private readonly RotateTransform _rotation = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly List<Visual> _trackedAncestors = [];
    private bool _isAttached;

    public BusyRing()
    {
        InitializeComponent();
        SpinRing.RenderTransform = _rotation;
        _timer.Tick += (_, _) =>
        {
            if (!IsEffectivelyVisible)
            {
                _timer.Stop();
                return;
            }

            _rotation.Angle = (_rotation.Angle + 6) % 360;
        };
        AttachedToVisualTree += (_, _) =>
        {
            _isAttached = true;
            SubscribeAncestorVisibility();
            UpdateTimerState();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            _isAttached = false;
            UnsubscribeAncestorVisibility();
            UpdateTimerState();
        };
        PropertyChanged += OnVisibilityPropertyChanged;
    }

    private void SubscribeAncestorVisibility()
    {
        UnsubscribeAncestorVisibility();
        foreach (var ancestor in this.GetVisualAncestors())
        {
            ancestor.PropertyChanged += OnVisibilityPropertyChanged;
            _trackedAncestors.Add(ancestor);
        }
    }

    private void UnsubscribeAncestorVisibility()
    {
        foreach (var ancestor in _trackedAncestors)
        {
            ancestor.PropertyChanged -= OnVisibilityPropertyChanged;
        }

        _trackedAncestors.Clear();
    }

    private void OnVisibilityPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsVisibleProperty)
        {
            UpdateTimerState();
        }
    }

    private void UpdateTimerState()
    {
        if (_isAttached && IsEffectivelyVisible)
        {
            _timer.Start();
        }
        else
        {
            _timer.Stop();
        }
    }
}
