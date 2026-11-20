using System.Globalization;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;

using DeepVisionSharp.Shell.Avalonia.Controls.Inference;
using DeepVisionSharp.Shell.Avalonia.ViewModels;

namespace DeepVisionSharp.Shell.Avalonia.Views;

/// <summary>
/// 推理工作台页：deepvision 版 InferencePage 的 Avalonia 移植，DataContext 换成本项目 stub，
/// 保留三栏自适应、画布缩放与检查器页签切换；不跑推理、不改工作列表状态。
/// </summary>
public partial class InferencePage : UserControl
{
    private const double TitleBarHeight = 44;
    private const double SourceSummaryBreakpoint = 820;

    private readonly SampleInferenceContext _context;

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
    {
        InitializeComponent();

        _context = new SampleInferenceContext();
        DataContext = _context;

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
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        if (_inferenceSurface is not null)
        {
            _inferenceSurface.ViewportChanged -= OnViewportChanged;
        }
    }

    private void OnPageSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        UpdateViewportConstraints();
        UpdateToolbarDensity();
    }

    private void OnViewportChanged(object? sender, EventArgs e)
    {
        if (_inferenceSurface is not null)
        {
            _context.ZoomScale = _inferenceSurface.ZoomScale;
        }
    }

    private void OnZoomInClicked(object? sender, RoutedEventArgs e) => _inferenceSurface?.ZoomIn();

    private void OnZoomOutClicked(object? sender, RoutedEventArgs e) => _inferenceSurface?.ZoomOut();

    private void OnFitClicked(object? sender, RoutedEventArgs e) => FitCanvas();

    private void OnInspectorConfigTabClicked(object? sender, RoutedEventArgs e) => SetInspectorTab(0);

    private void OnInspectorResultTabClicked(object? sender, RoutedEventArgs e) => SetInspectorTab(1);

    /// <summary>待推理列表项的右键菜单是本页的「预计 OK / NG」入口；stub 不回写标记，只保留菜单结构。</summary>
    private void OnImageItemContextRequested(object? sender, ContextRequestedEventArgs e)
    {
    }

    private void OnImageContextMenuClicked(object? sender, RoutedEventArgs e)
    {
    }

    private void SetInspectorTab(int index) => _context.SelectedRightTabIndex = index;

    private void FitCanvas()
    {
        if (_context.PreviewImage is null || _inferenceSurface is null)
        {
            return;
        }

        _inferenceSurface.FitToView();
        _context.ZoomScale = _inferenceSurface.ZoomScale;
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
