using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using DeepVisionSharp.Shell.Avalonia.Views;
using YuUI;
using YuUI.Controls;

namespace DeepVisionSharp.Shell.Avalonia;

/// <summary>外壳骨架：标题栏 + 导航栏 + 内容宿主。本波只做页面切换，不接业务导航服务与 DI。</summary>
public partial class MainWindow : ThemedWindow
{
    private const double ExpandedNavWidth = 208;
    private const double CollapsedNavWidth = 64;

    // 导航项文案同时是内容路由的键（与 XAML 里的 Content 一一对应）。
    private const string DashboardRoute = "项目";
    private const string GalleryRoute = "图库";
    private const string ModelsRoute = "模型";

    public static readonly StyledProperty<bool> IsNavCollapsedProperty =
        AvaloniaProperty.Register<MainWindow, bool>(nameof(IsNavCollapsed));

    public static readonly StyledProperty<bool> IsNavExpandedProperty =
        AvaloniaProperty.Register<MainWindow, bool>(nameof(IsNavExpanded), true);

    public static readonly StyledProperty<double> NavRailWidthProperty =
        AvaloniaProperty.Register<MainWindow, double>(nameof(NavRailWidth), ExpandedNavWidth);

    /// <summary>当前版本是否提供模型/训练/推理（Lite 版为 false，导航项整组隐藏）。</summary>
    public static readonly StyledProperty<bool> SupportsModelWorkflowProperty =
        AvaloniaProperty.Register<MainWindow, bool>(nameof(SupportsModelWorkflow), true);

    /// <summary>标题栏中部：当前打开的项目。本波没有工作区状态，保持「未打开项目」。</summary>
    public static readonly StyledProperty<string?> CurrentProjectNameProperty =
        AvaloniaProperty.Register<MainWindow, string?>(nameof(CurrentProjectName));

    public static readonly StyledProperty<bool> HasCurrentProjectProperty =
        AvaloniaProperty.Register<MainWindow, bool>(nameof(HasCurrentProject));

    /// <summary>路由 → 页面实例：切回同一导航项时复用，不重建页面。</summary>
    private readonly Dictionary<string, Control> _pages = new();

    static MainWindow()
    {
        IsNavCollapsedProperty.Changed.AddClassHandler<MainWindow>(
            (window, args) => window.ApplyNavState(args.GetNewValue<bool>()));
    }

    public MainWindow()
    {
        InitializeComponent();
        ApplyNavState(IsNavCollapsed);

        // 导航项只在 XAML 里声明，选中即切页：构造期统一挂 IsCheckedChanged，XAML 里的 IsChecked 不会先于内容宿主触发。
        foreach (var button in NavRail.GetLogicalDescendants().OfType<RadioButton>())
        {
            button.IsCheckedChanged += OnNavChecked;
        }

        ShowRoute(DashboardRoute, IvIcons.Home);
        ApplyThemeIcon(Application.Current?.ActualThemeVariant == ThemeVariant.Dark);
    }

    public bool IsNavCollapsed
    {
        get => GetValue(IsNavCollapsedProperty);
        set => SetValue(IsNavCollapsedProperty, value);
    }

    public bool SupportsModelWorkflow
    {
        get => GetValue(SupportsModelWorkflowProperty);
        set => SetValue(SupportsModelWorkflowProperty, value);
    }

    public bool IsNavExpanded
    {
        get => GetValue(IsNavExpandedProperty);
        private set => SetValue(IsNavExpandedProperty, value);
    }

    public double NavRailWidth
    {
        get => GetValue(NavRailWidthProperty);
        private set => SetValue(NavRailWidthProperty, value);
    }

    public string? CurrentProjectName
    {
        get => GetValue(CurrentProjectNameProperty);
        set => SetValue(CurrentProjectNameProperty, value);
    }

    public bool HasCurrentProject
    {
        get => GetValue(HasCurrentProjectProperty);
        set => SetValue(HasCurrentProjectProperty, value);
    }

    private void ApplyNavState(bool collapsed)
    {
        IsNavExpanded = !collapsed;
        NavRailWidth = collapsed ? CollapsedNavWidth : ExpandedNavWidth;
        NavRail.Width = NavRailWidth;
    }

    private void OnToggleNavigation(object? sender, RoutedEventArgs e) => IsNavCollapsed = !IsNavCollapsed;

    private void OnCaptionPointerPressed(object? sender, PointerPressedEventArgs e) => BeginCaptionDrag(e);

    private void OnCaptionDoubleTapped(object? sender, TappedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    /// <summary>主题偏好没有持久化，本波只在浅色/深色之间切换。</summary>
    private void OnToggleTheme(object? sender, RoutedEventArgs e)
    {
        if (Application.Current is not { } app)
        {
            return;
        }

        var useDark = app.ActualThemeVariant != ThemeVariant.Dark;
        app.RequestedThemeVariant = useDark ? ThemeVariant.Dark : ThemeVariant.Light;
        ApplyThemeIcon(useDark);
    }

    /// <summary>
    /// 图标表示「点下去会切到哪一档」：浅色档显示 Sparkles（IvIcons 没有月亮，用星夜顶替），深色档显示 Lamp（没有太阳，用灯顶替）。
    /// </summary>
    private void ApplyThemeIcon(bool useDark) => ThemeToggleIcon.Data = useDark ? IvIcons.Lamp : IvIcons.Sparkles;

    /// <summary>导航选中即切内容区：标记/训练/推理/设置还没有 Avalonia 页面，先给占位页。</summary>
    private void OnNavChecked(object? sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { IsChecked: true } button)
        {
            ShowRoute(button.Content?.ToString() ?? string.Empty, button.Tag as Geometry);
        }
    }

    private void ShowRoute(string route, Geometry? icon)
    {
        if (!_pages.TryGetValue(route, out var page))
        {
            page = CreatePage(route, icon);
            _pages.Add(route, page);
        }

        ContentHost.Content = page;
    }

    private Control CreatePage(string route, Geometry? icon) => route switch
    {
        DashboardRoute => new DashboardPage(),
        GalleryRoute => new GalleryPage(),
        ModelsRoute => new ModelsPage(),
        _ => CreatePlaceholderPage(route, icon),
    };

    private ContentControl CreatePlaceholderPage(string route, Geometry? icon) => new()
    {
        Theme = this.TryFindResource("PlaceholderPageTheme", out var theme) ? theme as ControlTheme : null,
        Content = $"{route} · 迁移中",
        Tag = icon,
    };
}
