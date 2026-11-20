using DeepVisionSharp.Presentation.Ports;

using System.Windows.Threading;

namespace DeepVisionSharp.Shell.Services;

/// <summary>
/// WPF <see cref="DispatcherTimer"/> adapter for the UI-thread timer port.
/// </summary>
public sealed class WpfUiTimerFactory : IUiTimerFactory
{
    public IUiTimer Create(TimeSpan interval) => new WpfUiTimer(interval);

    private sealed class WpfUiTimer : IUiTimer
    {
        private readonly DispatcherTimer _timer;

        public WpfUiTimer(TimeSpan interval)
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
