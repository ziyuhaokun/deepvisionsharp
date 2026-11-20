using Avalonia.Threading;
using DeepVisionSharp.Presentation.Ports;

namespace DeepVisionSharp.Shell.Avalonia.Services;

/// <summary>Avalonia <see cref="DispatcherTimer"/> 适配：ticks 落在 UI 线程，语义与 WPF 版一致。</summary>
public sealed class AvaloniaUiTimerFactory : IUiTimerFactory
{
    public IUiTimer Create(TimeSpan interval) => new AvaloniaUiTimer(interval);

    private sealed class AvaloniaUiTimer : IUiTimer
    {
        private readonly DispatcherTimer _timer;

        public AvaloniaUiTimer(TimeSpan interval)
        {
            _timer = new DispatcherTimer { Interval = interval };
            _timer.Tick += OnTick;
        }

        public event EventHandler? Tick;

        public bool IsEnabled => _timer.IsEnabled;

        public void Start() => _timer.Start();

        public void Stop() => _timer.Stop();

        public void Dispose()
        {
            _timer.Stop();
            _timer.Tick -= OnTick;
            Tick = null;
        }

        private void OnTick(object? sender, EventArgs e) => Tick?.Invoke(this, e);
    }
}
