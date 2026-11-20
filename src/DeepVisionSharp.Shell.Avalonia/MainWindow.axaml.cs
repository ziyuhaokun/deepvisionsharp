using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.VisualTree;
using DeepVisionSharp.Shell.Avalonia.Views;
using YuUI.Controls;

namespace DeepVisionSharp.Shell.Avalonia;

/// <summary>外壳骨架：标题栏 + 导航栏 + 内容宿主。本波只做页面切换，不接业务导航服务与 DI。</summary>
public partial class MainWindow : ThemedWindow
{
    private const double ExpandedNavWidth = 208;
    private const double CollapsedNavWidth = 64;
    private const string DashboardLabel = "项目";

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

    static MainWindow()
    {
        IsNavCollapsedProperty.Changed.AddClassHandler<MainWindow>(
            (window, args) => window.ApplyNavState(args.GetNewValue<bool>()));
    }

    public MainWindow()
    {
        InitializeComponent();
        ApplyNavState(IsNavCollapsed);

        Loaded += (_, _) =>
        {
            foreach (var button in this.GetVisualDescendants().OfType<RadioButton>())
            {
                button.IsCheckedChanged += OnNavChecked;
            }

            if (this.GetVisualDescendants().OfType<RadioButton>().FirstOrDefault(b => b.IsChecked == true) is { } initial)
            {
                OnNavChecked(initial, new RoutedEventArgs());
            }
        };
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
        if (Application.Current is { } app)
        {
            app.RequestedThemeVariant = app.ActualThemeVariant == ThemeVariant.Dark
                ? ThemeVariant.Light
                : ThemeVariant.Dark;
        }
    }

    /// <summary>导航只切内容宿主里的页面：本波只有项目页，其余路由给占位页。</summary>
    private void OnNavChecked(object? sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { IsChecked: true } button)
        {
            return;
        }

        var label = button.Content?.ToString() ?? string.Empty;
        ContentHost.Content = label == DashboardLabel
            ? ContentHost.Content as DashboardPage ?? new DashboardPage()
            : CreatePlaceholderPage(label);
    }

    private static TextBlock CreatePlaceholderPage(string label) => new()
    {
        Text = $"{label}页面（迁移中）",
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };
}
