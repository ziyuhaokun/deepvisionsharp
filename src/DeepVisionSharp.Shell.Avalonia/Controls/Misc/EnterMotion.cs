using System.Diagnostics;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace DeepVisionSharp.Shell.Avalonia.Controls;

/// <summary>
/// 「出现时淡入上移」动效：给分区容器设 <c>EnterMotion.PlayOnShow="True"</c>，
/// 之后每次 <c>IsVisible</c> 由 false 变 true（tab 切换、详情卡重新显示）就播一次。
/// <para>
/// 不用 Avalonia 的 Animation：这套 Avalonia 没有注册 RenderTransform 的动画器
/// （<c>TransformOperationsAnimator</c> 不是公开类型），带 transform 的关键帧会在启动时抛
/// 「No animator registered for the property RenderTransform」。所以和 BusyRing 一样，
/// 由 UI 线程计时器逐帧写 Opacity 与 TranslateTransform。
/// </para>
/// 连续触发会从头再来；播放期间改过 RenderTransform（被别人占用）时只做淡入，不覆盖对方。
/// </summary>
public sealed class EnterMotion : AvaloniaObject
{
    /// <summary>tab 分区的默认节奏：短而轻，频繁切换也不拖沓。</summary>
    public static readonly TimeSpan DefaultDuration = TimeSpan.FromMilliseconds(180);

    public const double DefaultOffsetY = 8;

    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(16);

    public static readonly AttachedProperty<bool> PlayOnShowProperty =
        AvaloniaProperty.RegisterAttached<EnterMotion, Control, bool>("PlayOnShow");

    private static readonly ConditionalWeakTable<Control, Run> Running = new();

    private EnterMotion()
    {
    }

    static EnterMotion()
    {
        PlayOnShowProperty.Changed.AddClassHandler<Control>(OnPlayOnShowChanged);
    }

    public static bool GetPlayOnShow(Control control) => control.GetValue(PlayOnShowProperty);

    public static void SetPlayOnShow(Control control, bool value) => control.SetValue(PlayOnShowProperty, value);

    private static void OnPlayOnShowChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        control.PropertyChanged -= OnControlPropertyChanged;
        if (e.NewValue is true)
        {
            control.PropertyChanged += OnControlPropertyChanged;
        }
    }

    private static void OnControlPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (sender is Control control && e.Property == Visual.IsVisibleProperty && e.NewValue is true)
        {
            Play(control);
        }
    }

    /// <summary>淡入（0→1）加上移（offset→0）；<paramref name="duration"/> 缺省 180ms。</summary>
    public static void Play(Control target, TimeSpan? duration = null, double offsetY = DefaultOffsetY)
    {
        // 连续触发：先把上一次收尾（还原到原始状态），再从头来。
        if (Running.TryGetValue(target, out var previous))
        {
            previous.Finish();
        }

        var run = new Run(target, duration ?? DefaultDuration, offsetY);
        Running.AddOrUpdate(target, run);
        run.Start();
    }

    private sealed class Run
    {
        private readonly Control _target;
        private readonly TimeSpan _duration;
        private readonly double _offsetY;
        private readonly double _originalOpacity;
        private readonly object? _originalTransform;
        private readonly TranslateTransform? _translate;
        private readonly bool _ownsTransform;
        private readonly DispatcherTimer _timer = new(DispatcherPriority.Render) { Interval = FrameInterval };
        private readonly Stopwatch _clock = new();
        private bool _finished;

        public Run(Control target, TimeSpan duration, double offsetY)
        {
            _target = target;
            _duration = duration;
            _offsetY = offsetY;
            _originalOpacity = target.Opacity;
            _originalTransform = target.RenderTransform;

            // 位移只借用「没人用」或「本来就是 TranslateTransform」的 RenderTransform。
            if (target.RenderTransform is null)
            {
                _translate = new TranslateTransform();
                _ownsTransform = true;
            }
        }

        public void Start()
        {
            // 同步把起点写好，避免先以完整状态闪一帧再开始。
            Apply(0);
            if (_target.RenderTransform is null && _translate is not null)
            {
                _target.RenderTransform = _translate;
            }

            _timer.Tick += OnTick;
            _clock.Start();
            _timer.Start();
        }

        private void OnTick(object? sender, EventArgs e)
        {
            if (_finished)
            {
                return;
            }

            // 页面被换走 / 隐藏了：不用继续算，直接收尾。
            if (!_target.IsEffectivelyVisible)
            {
                Finish();
                return;
            }

            var progress = Math.Clamp(_clock.Elapsed.TotalMilliseconds / _duration.TotalMilliseconds, 0, 1);
            if (progress >= 1)
            {
                Finish();
                return;
            }

            Apply(progress);
        }

        private void Apply(double progress)
        {
            var eased = 1 - Math.Pow(1 - progress, 3); // CubicEaseOut
            _target.Opacity = _originalOpacity * eased;
            if (_translate is not null)
            {
                _translate.Y = _offsetY * (1 - eased);
            }
        }

        public void Finish()
        {
            if (_finished)
            {
                return;
            }

            _finished = true;
            _timer.Stop();
            _timer.Tick -= OnTick;
            _clock.Stop();

            _target.Opacity = _originalOpacity;
            if (_ownsTransform && ReferenceEquals(_target.RenderTransform, _translate))
            {
                _target.RenderTransform = _originalTransform as ITransform;
            }

            if (Running.TryGetValue(_target, out var current) && ReferenceEquals(current, this))
            {
                Running.Remove(_target);
            }
        }
    }
}
