using CommunityToolkit.Mvvm.ComponentModel;

using DeepVisionSharp.Presentation.Services;
using DeepVisionSharp.Presentation.Ports;

namespace DeepVisionSharp.Presentation.ViewModels.Training;

/// <summary>
/// Host CPU/GPU/memory resource gauges for the training run panel.
/// Owned by <see cref="TrainingMonitorViewModel"/>.
/// </summary>
public sealed partial class TrainingResourcePanelViewModel : ObservableObject
{
    private readonly TrainingResourcePoller _resourcePolling;
    private readonly IUiDispatcher _uiDispatcher;
    private Func<bool>? _isResourcesSectionActive;

    /// <summary>折线图保留最近多久的采样；采样间隔 2 秒，5 分钟约 150 个点。</summary>
    internal static readonly TimeSpan HistoryWindow = TimeSpan.FromMinutes(5);

    /// <summary>离开资源页超过这么久再回来，旧曲线和新点之间会隔出一大段空白，直接重新开始。</summary>
    private static readonly TimeSpan StaleGap = TimeSpan.FromSeconds(10);

    private readonly List<TrainingChartPoint> _cpuHistory = [];
    private readonly List<TrainingChartPoint> _memoryHistory = [];
    private readonly List<TrainingChartPoint> _gpuHistory = [];
    private Func<DateTimeOffset> _clock = () => DateTimeOffset.UtcNow;

    public TrainingResourcePanelViewModel(
        TrainingResourcePoller resourcePolling,
        IUiDispatcher uiDispatcher)
    {
        _resourcePolling = resourcePolling;
        _uiDispatcher = uiDispatcher;
    }

    [ObservableProperty]
    private double _resourceCpuPercent;

    [ObservableProperty]
    private double _resourceMemoryPercent;

    [ObservableProperty]
    private double _resourceGpuPercent;

    [ObservableProperty]
    private bool _hasResourceData;

    /// <summary>
    /// 时间轴折线图的数据：横轴是 Unix 秒（放在 <see cref="TrainingChartPoint.Epoch"/> 里），纵轴是占用百分比。
    /// 每次采样换一份新列表，图表控件靠引用变化重绘。
    /// </summary>
    [ObservableProperty]
    private IReadOnlyList<TrainingChartSeries> _usageSeries = [];

    [ObservableProperty]
    private bool _hasGpuResourceData;

    [ObservableProperty]
    private string? _resourcePollingError;

    /// <summary>数值带的列数：有 GPU 数据时 3 列，否则 2 列。</summary>
    public int GaugeColumns => HasGpuResourceData ? 3 : 2;

    public bool HasResourcePollingError => !string.IsNullOrWhiteSpace(ResourcePollingError);

    public void AttachSectionGate(Func<bool> isResourcesSectionActive) =>
        _isResourcesSectionActive = isResourcesSectionActive;

    public void StartPolling()
    {
        ResourcePollingError = null;
        DropStaleHistory();

        _resourcePolling.Start(
            () => _isResourcesSectionActive?.Invoke() ?? false,
            (cpu, memory, gpu, hasGpu) =>
            {
                ResourceCpuPercent = cpu;
                ResourceMemoryPercent = memory;
                ResourceGpuPercent = gpu;
                HasGpuResourceData = hasGpu;
                HasResourceData = true;
                RecordSample(cpu, memory, gpu, hasGpu);
            },
            ReportPollingError);
    }

    public void StopPolling() => _resourcePolling.Stop();

    /// <summary>记一次采样并重建折线图数据；超出时间窗口的旧点丢掉。测试可直接调用。</summary>
    internal void RecordSample(double cpu, double memory, double gpu, bool hasGpu)
    {
        var now = _clock();
        var x = (int)now.ToUnixTimeSeconds();
        var oldest = (int)(now - HistoryWindow).ToUnixTimeSeconds();

        Append(_cpuHistory, x, cpu, oldest);
        Append(_memoryHistory, x, memory, oldest);
        if (hasGpu)
        {
            Append(_gpuHistory, x, gpu, oldest);
        }
        else
        {
            _gpuHistory.Clear();
        }

        var series = new List<TrainingChartSeries>(3)
        {
            new("CPU", _cpuHistory.ToArray()),
            new("内存", _memoryHistory.ToArray()),
        };
        if (_gpuHistory.Count > 0)
        {
            series.Add(new TrainingChartSeries("GPU", _gpuHistory.ToArray()));
        }

        UsageSeries = series;
    }

    internal void UseClock(Func<DateTimeOffset> clock) => _clock = clock;

    private void DropStaleHistory()
    {
        if (_cpuHistory.Count == 0)
        {
            return;
        }

        var last = DateTimeOffset.FromUnixTimeSeconds(_cpuHistory[^1].Epoch);
        if (_clock() - last <= StaleGap)
        {
            return;
        }

        _cpuHistory.Clear();
        _memoryHistory.Clear();
        _gpuHistory.Clear();
        UsageSeries = [];
    }

    private static void Append(List<TrainingChartPoint> history, int x, double value, int oldest)
    {
        // 同一秒内重复采样（切页时会立刻补采一次）只保留最新值，避免同一个 x 上出现两个点。
        if (history.Count > 0 && history[^1].Epoch == x)
        {
            history[^1] = new TrainingChartPoint(x, value);
        }
        else
        {
            history.Add(new TrainingChartPoint(x, value));
        }

        var drop = 0;
        while (drop < history.Count - 1 && history[drop].Epoch < oldest)
        {
            drop++;
        }

        if (drop > 0)
        {
            history.RemoveRange(0, drop);
        }
    }

    partial void OnHasGpuResourceDataChanged(bool value) => OnPropertyChanged(nameof(GaugeColumns));

    partial void OnResourcePollingErrorChanged(string? value) =>
        OnPropertyChanged(nameof(HasResourcePollingError));

    private void ReportPollingError(Exception ex)
    {
        _ = _uiDispatcher.InvokeAsync(() =>
        {
            ResourcePollingError = $"资源轮询失败：{ex.Message}";
        });
    }
}
