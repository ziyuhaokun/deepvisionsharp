using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using DeepVisionSharp.Presentation.ViewModels.Models;
using DeepVisionSharp.Shell.Avalonia.ViewModels;

namespace DeepVisionSharp.Shell.Avalonia.Views;

/// <summary>
/// 模型页。DataContext 来自容器的 <see cref="ModelsViewModel"/>；
/// 没有容器（设计时 / 无头渲染）时退回 <see cref="SampleModelsContext"/>，绑定路径不变。
/// </summary>
public partial class ModelsPage : UserControl
{
    private readonly ModelsViewModel? _viewModel;

    public ModelsPage()
        : this(null)
    {
    }

    public ModelsPage(ModelsViewModel? viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = (object?)viewModel ?? new SampleModelsContext();

        Loaded += OnLoaded;
    }

    /// <summary>每次进入都重新加载，让项目/会话切换后的列表保持最新（与 WPF 页一致）。</summary>
    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        try
        {
            await _viewModel.OnPageLoadedAsync();
        }
        catch (OperationCanceledException)
        {
            // 导航走了／页面被取消，下一次进入会重新加载。
        }
        catch (Exception ex)
        {
            // async void：逃出去的异常会打断整个 UI 线程，这里就地记下。
            Trace.WriteLine($"ModelsPage load failed: {ex}");
        }
    }
}
