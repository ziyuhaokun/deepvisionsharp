using System.Globalization;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Interactivity;

using DeepVisionSharp.Presentation.ViewModels.Inference;
using DeepVisionSharp.Shell.Avalonia.Controls.Inference;
using DeepVisionSharp.Shell.Avalonia.ViewModels;

namespace DeepVisionSharp.Shell.Avalonia.Views;

/// <summary>
/// 推理工作台页：deepvision 版 InferencePage 的 Avalonia 移植。
/// DataContext 来自容器的 <see cref="InferenceViewModel"/>；没有容器（设计时 / 无头渲染）时
/// 退回 <see cref="SampleInferenceContext"/>，绑定路径不变。
/// 三栏自适应、画布缩放与检查器页签切换照原样保留。
/// </summary>
public partial class InferencePage : UserControl
{
    private const double TitleBarHeight = 44;
    private const double SourceSummaryBreakpoint = 820;

    private readonly InferenceViewModel? _viewModel;
    private readonly SampleInferenceContext? _stub;

    private readonly Grid? _pageRoot;
    private readonly Grid? _workbenchGrid;
    private readonly Border? _paneToolbar;
    private readonly Border? _leftListPanel;
    private readonly Border? _detailsPane;
    private readonly StackPanel? _sourceSummaryPanel;
    private readonly Button? _toolsPaneToggle;
    private readonly Button? _detailsPaneToggle;
    private readonly AnnotationCanvas? _inferenceSurface;

    public InferencePage()
        : this(null)
    {
    }

    public InferencePage(InferenceViewModel? viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        _stub = viewModel is null ? new SampleInferenceContext() : null;
        DataContext = (object?)viewModel ?? _stub;

        _pageRoot = this.FindControl<Grid>("PageRoot");
        _workbenchGrid = this.FindControl<Grid>("WorkbenchGrid");
        _paneToolbar = this.FindControl<Border>("PaneToolbar");
        _leftListPanel = this.FindControl<Border>("LeftListPanel");
        _detailsPane = this.FindControl<Border>("DetailsPane");
        _sourceSummaryPanel = this.FindControl<StackPanel>("SourceSummaryPanel");
        _toolsPaneToggle = this.FindControl<Button>("ToolsPaneToggle");
        _detailsPaneToggle = this.FindControl<Button>("DetailsPaneToggle");
        _inferenceSurface = this.FindControl<AnnotationCanvas>("InferenceSurface");

        if (_workbenchGrid is not null
            && _leftListPanel is not null
            && _detailsPane is not null
            && _paneToolbar is not null
            && _toolsPaneToggle is not null
            && _detailsPaneToggle is not null)
        {
            _ = new AdaptiveWorkbenchLayout(
                _workbenchGrid,
                _leftListPanel,
                _detailsPane,
                _paneToolbar,
                _toolsPaneToggle,
                _detailsPaneToggle);
        }
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (_inferenceSurface is not null)
        {
            _inferenceSurface.ViewportChanged -= OnViewportChanged;
            _inferenceSurface.ViewportChanged += OnViewportChanged;
        }

        UpdateViewportConstraints();
        UpdateToolbarDensity();
        FitCanvas();

        if (_viewModel is not null)
        {
            LoadViewModelAsync();
        }
    }

    /// <summary>async void 里的加载：异常就地记下，不往外抛到 UI 线程。</summary>
    private async void LoadViewModelAsync()
    {
        try
        {
            _viewModel!.Activate();
            await _viewModel.OnPageLoadedAsync();
        }
        catch (OperationCanceledException)
        {
            // 导航走了／页面被取消，下一次进入会重新加载。
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"InferencePage load failed: {ex}");
        }

        FitCanvas();
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        if (_inferenceSurface is not null)
        {
            _inferenceSurface.ViewportChanged -= OnViewportChanged;
        }

        _viewModel?.Deactivate();
    }

    private void OnPageSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        UpdateViewportConstraints();
        UpdateToolbarDensity();
    }

    private void OnViewportChanged(object? sender, EventArgs e)
    {
        if (_inferenceSurface is null)
        {
            return;
        }

        var zoomScale = _inferenceSurface.ZoomScale;
        if (_viewModel is not null)
        {
            _viewModel.ZoomScale = zoomScale;
        }

        if (_stub is not null)
        {
            _stub.ZoomScale = zoomScale;
        }
    }

    private void OnZoomInClicked(object? sender, RoutedEventArgs e) => _inferenceSurface?.ZoomIn();

    private void OnZoomOutClicked(object? sender, RoutedEventArgs e) => _inferenceSurface?.ZoomOut();

    private void OnFitClicked(object? sender, RoutedEventArgs e) => FitCanvas();

    private void OnInspectorConfigTabClicked(object? sender, RoutedEventArgs e) => SetInspectorTab(0);

    private void OnInspectorResultTabClicked(object? sender, RoutedEventArgs e) => SetInspectorTab(1);

    private void SetInspectorTab(int index)
    {
        if (_viewModel is not null)
        {
            _viewModel.SelectedRightTabIndex = index;
            return;
        }

        _stub!.SelectedRightTabIndex = index;
    }

    private void FitCanvas()
    {
        var previewImage = _viewModel is not null ? _viewModel.PreviewImage : _stub!.PreviewImage;
        if (previewImage is null || _inferenceSurface is null)
        {
            return;
        }

        _inferenceSurface.FitToView();

        if (_stub is not null)
        {
            _stub.ZoomScale = _inferenceSurface.ZoomScale;
        }
    }

    /// <summary>页面高度锁到「窗口客户区减标题栏减页边距」，让三栏区自己滚动而不是把整页撑出窗口。</summary>
    private void UpdateViewportConstraints()
    {
        if (_pageRoot is null)
        {
            return;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        var clientHeight = topLevel?.ClientSize.Height ?? 0;
        if (clientHeight <= 0)
        {
            return;
        }

        var verticalMargin = _pageRoot.Margin.Top + _pageRoot.Margin.Bottom;
        var pageHeight = clientHeight - TitleBarHeight - verticalMargin;
        if (pageHeight <= 0)
        {
            return;
        }

        _pageRoot.MaxHeight = pageHeight;
        _pageRoot.Height = pageHeight;
    }

    /// <summary>窗口变窄时把顶部的图片来源摘要压成 0 宽（不换行、不挤压按钮组）。</summary>
    private void UpdateToolbarDensity()
    {
        if (_sourceSummaryPanel is null || Bounds.Width <= 0)
        {
            return;
        }

        _sourceSummaryPanel.Width = Bounds.Width < SourceSummaryBreakpoint ? 0 : double.NaN;
    }
}

/// <summary>比较整数值与 ConverterParameter，供右侧页签切换使用。</summary>
public sealed class IndexEqualsConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null || parameter is null)
        {
            return false;
        }

        return System.Convert.ToInt32(value, CultureInfo.InvariantCulture)
            == System.Convert.ToInt32(parameter, CultureInfo.InvariantCulture);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>数量为 0 时返回 true，供待推理列表空状态使用。</summary>
public sealed class CountIsZeroConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not null && System.Convert.ToInt32(value, CultureInfo.InvariantCulture) == 0;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
