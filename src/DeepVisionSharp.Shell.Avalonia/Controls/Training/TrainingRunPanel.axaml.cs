using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;
using DeepVisionSharp.Presentation.ViewModels.Training;
using DeepVisionSharp.Shell.Avalonia.ViewModels;

namespace DeepVisionSharp.Shell.Avalonia.Controls.Training;

/// <summary>
/// 训练运行面板：配置预览、结果、预览、指标、资源、日志六个分区与图片灯箱。
/// 日志区沿用 deepvision 行为：勾选「自动滚动」时随 LiveLog 变化滚到底部。
/// </summary>
public partial class TrainingRunPanel : UserControl
{
    private INotifyPropertyChanged? _log;
    private Func<bool>? _isAutoScrollEnabled;

    public TrainingRunPanel()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>运行日志的滚动容器（XAML 里命名 LogScrollViewer，与 WPF 版一致）。</summary>
    private ScrollViewer? LogScroll => this.FindControl<ScrollViewer>("LogScrollViewer");

    private void OnLoaded(object? sender, EventArgs e) => AttachLog();

    private void OnDataContextChanged(object? sender, EventArgs e) => AttachLog();

    private void AttachLog()
    {
        DetachLog();

        switch (DataContext)
        {
            case TrainingViewModel viewModel:
                _log = viewModel.Log;
                _isAutoScrollEnabled = () => viewModel.Log.AutoScrollLog;
                break;
            case SampleTrainingContext sample:
                _log = sample.Log;
                _isAutoScrollEnabled = () => sample.Log.AutoScrollLog;
                break;
            default:
                return;
        }

        _log.PropertyChanged += OnLogPropertyChanged;
        ScrollToLogEndIfEnabled();
    }

    private void DetachLog()
    {
        if (_log is not null)
        {
            _log.PropertyChanged -= OnLogPropertyChanged;
        }

        _log = null;
        _isAutoScrollEnabled = null;
    }

    private void OnUnloaded(object? sender, EventArgs e) => DetachLog();

    private void OnLogPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SampleTrainingLog.LiveLog))
        {
            ScrollToLogEndIfEnabled();
        }
    }

    private void ScrollToLogEndIfEnabled()
    {
        if (_isAutoScrollEnabled is not { } isAutoScrollEnabled || !isAutoScrollEnabled())
        {
            return;
        }

        // 等这一轮文本布局完成后再滚，否则 ExtentHeight 还是旧值
        Dispatcher.UIThread.Post(
            () => LogScroll?.ScrollToEnd(),
            DispatcherPriority.Loaded);
    }
}
