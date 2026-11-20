using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace DeepVisionSharp.Shell.Avalonia.Controls.Training;

/// <summary>忙碌环：绕中心匀速旋转，由 UI 线程计时器驱动（Avalonia 没有 RenderTransform 动画器）。</summary>
public partial class BusyRing : UserControl
{
    private readonly RotateTransform _rotation = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };

    public BusyRing()
    {
        InitializeComponent();
        SpinRing.RenderTransform = _rotation;
        _timer.Tick += (_, _) => _rotation.Angle = (_rotation.Angle + 6) % 360;
        AttachedToVisualTree += (_, _) => _timer.Start();
        DetachedFromVisualTree += (_, _) => _timer.Stop();
    }
}
