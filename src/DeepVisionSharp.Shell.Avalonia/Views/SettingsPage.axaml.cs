using Avalonia.Controls;
using Avalonia.Interactivity;
using DeepVisionSharp.Presentation.ViewModels;
using DeepVisionSharp.Shell.Avalonia.ViewModels;

namespace DeepVisionSharp.Shell.Avalonia.Views;

/// <summary>
/// 设置页：外观与通知 / 存储 / 关于 / 高级（F12 解锁）四个分区，另加 Python 环境分区。
/// DataContext 来自容器的 <see cref="SettingsViewModel"/>；没有容器（设计时 / 无头渲染）时
/// 退回 <see cref="SampleSettingsContext"/>，绑定路径不变。
/// </summary>
public partial class SettingsPage : UserControl
{
    private readonly SettingsViewModel? _viewModel;
    private bool _loaded;

    public SettingsPage()
        : this(null)
    {
    }

    public SettingsPage(SettingsViewModel? viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = (object?)viewModel ?? new SampleSettingsContext();

        Loaded += OnLoaded;
    }

    /// <summary>存储占用扫描只做一次：切回本页不需要重新扫盘（页实例由导航缓存）。</summary>
    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null || _loaded)
        {
            return;
        }

        _loaded = true;
        try
        {
            await _viewModel.LoadCommand.ExecuteAsync(null);
        }
        catch (OperationCanceledException)
        {
            // 导航走了／页面被取消，下一次进入会重新加载。
        }
        catch (Exception ex)
        {
            // async void：逃出去的异常会打断整个 UI 线程，这里就地记下。
            System.Diagnostics.Trace.WriteLine($"SettingsPage load failed: {ex}");
        }
    }
}
