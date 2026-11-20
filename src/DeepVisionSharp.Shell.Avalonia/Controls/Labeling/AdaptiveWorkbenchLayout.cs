using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.VisualTree;

namespace DeepVisionSharp.Shell.Avalonia.Controls.Labeling;

/// <summary>
/// 工作区三栏（工具 | 主体 | 详情）的窄窗口自适应：宽度不够时收起工具/详情栏，
/// 顶部出现「工具」「详情」按钮，收起后以浮层形式覆盖在主体上。
/// 对应 WPF 版 Services/AdaptiveWorkbenchLayout；推理页与标注页共用。
/// </summary>
internal sealed class AdaptiveWorkbenchLayout
{
    private readonly Grid _grid;
    private readonly Border _tools;
    private readonly Border _details;
    private readonly Control _toolbar;
    private readonly Button _toolsToggle;
    private readonly Button _detailsToggle;
    private readonly double _toolsWidth;
    private readonly double _detailsWidth;
    private readonly string _toolsLabel;
    private readonly string _detailsLabel;
    private bool _toolsOpen;
    private bool _detailsOpen;
    private bool _compact;
    private bool _narrow;

    public AdaptiveWorkbenchLayout(
        Grid grid,
        Border tools,
        Border details,
        Control toolbar,
        Button toolsToggle,
        Button detailsToggle)
    {
        _grid = grid;
        _tools = tools;
        _details = details;
        _toolbar = toolbar;
        _toolsToggle = toolsToggle;
        _detailsToggle = detailsToggle;
        _toolsWidth = grid.ColumnDefinitions[0].Width.Value;
        _detailsWidth = grid.ColumnDefinitions[2].Width.Value;
        _toolsLabel = toolsToggle.Content?.ToString() ?? "工具";
        _detailsLabel = detailsToggle.Content?.ToString() ?? "详情";

        grid.SizeChanged += (_, _) => Update();
        toolsToggle.Click += (_, _) =>
        {
            _toolsOpen = !_toolsOpen;
            _detailsOpen = false;
            Update();
        };
        detailsToggle.Click += (_, _) =>
        {
            _detailsOpen = !_detailsOpen;
            _toolsOpen = false;
            Update();
        };
        grid.AddHandler(
            InputElement.KeyDownEvent,
            new EventHandler<KeyEventArgs>(OnKeyDown),
            RoutingStrategies.Tunnel);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || e.Key != Key.Escape || (!_toolsOpen && !_detailsOpen))
        {
            return;
        }

        if (!IsFocusWithin(_tools) && !IsFocusWithin(_details))
        {
            return;
        }

        _toolsOpen = false;
        _detailsOpen = false;
        Update();
        e.Handled = true;
    }

    private static bool IsFocusWithin(Control pane)
    {
        var focused = TopLevel.GetTopLevel(pane)?.FocusManager?.GetFocusedElement() as Visual;
        if (focused is null)
        {
            return false;
        }

        return ReferenceEquals(focused, pane) || pane.IsVisualAncestorOf(focused);
    }

    private void Update()
    {
        var width = _grid.Bounds.Width;
        if (width <= 0)
        {
            return;
        }

        var compact = width < _toolsWidth + _detailsWidth + 480;
        var narrow = width < _toolsWidth + 480;
        if (compact != _compact)
        {
            _detailsOpen = false;
        }

        if (narrow != _narrow)
        {
            _toolsOpen = false;
        }

        _compact = compact;
        _narrow = narrow;

        _grid.ColumnDefinitions[0].Width = new GridLength(narrow ? 0 : _toolsWidth);
        _grid.ColumnDefinitions[2].Width = new GridLength(compact ? 0 : _detailsWidth);
        _toolbar.IsVisible = compact;
        _toolsToggle.IsVisible = narrow;
        _detailsToggle.IsVisible = compact;

        ConfigurePane(
            _tools,
            narrow,
            _toolsOpen,
            0,
            narrow ? 2 : 1,
            Math.Min(_toolsWidth, Math.Max(0, width - 16)),
            HorizontalAlignment.Left);
        ConfigurePane(
            _details,
            compact,
            _detailsOpen,
            compact ? 1 : 2,
            compact ? 2 : 1,
            Math.Min(_detailsWidth, Math.Max(0, width - (narrow ? 0 : _toolsWidth) - 16)),
            HorizontalAlignment.Right);

        _toolsToggle.Content = _toolsOpen ? "收起" + _toolsLabel : _toolsLabel;
        _detailsToggle.Content = _detailsOpen ? "收起" + _detailsLabel : _detailsLabel;
    }

    private static void ConfigurePane(
        Border pane,
        bool overlay,
        bool open,
        int column,
        int span,
        double width,
        HorizontalAlignment alignment)
    {
        pane.IsVisible = !overlay || open;
        Grid.SetColumn(pane, column);
        Grid.SetColumnSpan(pane, span);
        pane.ZIndex = overlay ? 20 : 0;
        pane.Width = overlay ? width : double.NaN;
        pane.HorizontalAlignment = overlay ? alignment : HorizontalAlignment.Stretch;
        pane.Classes.Set("paneOverlay", overlay);
        pane.Classes.Set("paneSurface", !overlay);
    }
}
