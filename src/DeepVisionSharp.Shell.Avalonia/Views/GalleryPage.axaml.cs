using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Styling;
using DeepVisionSharp.Presentation.ViewModels;
using DeepVisionSharp.Shell.Avalonia.ViewModels;

namespace DeepVisionSharp.Shell.Avalonia.Views;

/// <summary>
/// 图库页。DataContext 来自容器的 <see cref="GalleryViewModel"/>；
/// 没有容器（设计时 / 无头渲染）时退回 <see cref="SampleGalleryContext"/>，绑定路径不变。
/// </summary>
public partial class GalleryPage : UserControl
{
    private readonly GalleryViewModel? _viewModel;
    private readonly ICardColumnsHost _cardColumns;

    public GalleryPage()
        : this(null)
    {
    }

    public GalleryPage(GalleryViewModel? viewModel)
    {
        InitializeComponent();

        // Restore the Fluent MenuItem template for this page's flyout, including nested items.
        if (global::Avalonia.Application.Current?.Styles.TryGetResource(typeof(MenuItem), null, out var resource) == true
            && resource is ControlTheme menuItemTheme)
        {
            Resources[typeof(MenuItem)] = menuItemTheme;
        }

        _viewModel = viewModel;
        var context = (object?)viewModel ?? new SampleGalleryContext();
        DataContext = context;
        _cardColumns = (ICardColumnsHost)context;

        SizeChanged += OnPageSizeChanged;
        Loaded += OnLoaded;
    }

    /// <summary>与 deepvision 同一套分栏公式：可用宽度按 380 一分栏，1~4 栏。</summary>
    private void OnPageSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        var availableWidth = Math.Max(0, Bounds.Width - 64);
        _cardColumns.CardColumns = availableWidth > 0
            ? Math.Clamp((int)Math.Floor(availableWidth / 380), 1, 4)
            : 1;
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
            await _viewModel.LoadCommand.ExecuteAsync(null);
        }
        catch (OperationCanceledException)
        {
            // 导航走了／页面被取消，下一次进入会重新加载。
        }
        catch (Exception ex)
        {
            // async void：逃出去的异常会打断整个 UI 线程，这里就地记下。
            Trace.WriteLine($"GalleryPage load failed: {ex}");
        }
    }
}
