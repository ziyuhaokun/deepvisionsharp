using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using DeepVisionSharp.Presentation.Ports;
using DeepVisionSharp.Presentation.Services;
using DeepVisionSharp.Presentation.Services.Navigation;
using DeepVisionSharp.Presentation.ViewModels;
using DeepVisionSharp.Shell.Avalonia.Composition;
using DeepVisionSharp.Shell.Avalonia.Services;
using DeepVisionSharp.Shell.Avalonia.Services.Hosting;
using Microsoft.Extensions.DependencyInjection;
using YuUI;
using YuUI.Controls;

namespace DeepVisionSharp.Shell.Avalonia;

/// <summary>
/// 外壳窗口，同时是内容区宿主（<see cref="IPageHost"/>）与提示条绘制方。
/// 导航走 <see cref="AvaloniaNavigationService"/>（路由 → 页面），对话框走主窗口里的 <c>RootDialog</c>。
/// </summary>
public partial class MainWindow : ThemedWindow, IPageHost
{
    private const double ExpandedNavWidth = 208;
    private const double CollapsedNavWidth = 64;
    private static readonly TimeSpan DefaultSnackbarDuration = TimeSpan.FromSeconds(3);

    /// <summary>导航项文案 → 路由；文案就是 XAML 里的 <c>Content</c>，也是占位页的标题。</summary>
    private static readonly IReadOnlyDictionary<string, AppRoute> RoutesByLabel =
        new Dictionary<string, AppRoute>(StringComparer.Ordinal)
        {
            ["项目"] = AppRoute.Dashboard,
            ["图库"] = AppRoute.Gallery,
            ["标记"] = AppRoute.Labeling,
            ["模型"] = AppRoute.Models,
            ["训练"] = AppRoute.Training,
            ["推理"] = AppRoute.Inference,
            ["设置"] = AppRoute.Settings,
        };

    private readonly IServiceProvider? _services;
    private readonly AvaloniaNavigationService _navigation;
    private readonly MainWindowViewModel? _viewModel;
    private readonly AvaloniaSnackbarHost? _snackbarHost;
    private readonly IAppThemeService? _themeService;
    private readonly ShellSettingsService? _settingsService;
    private readonly Dictionary<AppRoute, RadioButton> _navButtons = new();
    private readonly DispatcherTimer _snackbarTimer = new();

    private bool _isSyncingNavSelection;
    private int _snackbarGeneration;

    public static readonly StyledProperty<bool> IsNavCollapsedProperty =
        AvaloniaProperty.Register<MainWindow, bool>(nameof(IsNavCollapsed));

    public static readonly StyledProperty<bool> IsNavExpandedProperty =
        AvaloniaProperty.Register<MainWindow, bool>(nameof(IsNavExpanded), true);

    public static readonly StyledProperty<double> NavRailWidthProperty =
        AvaloniaProperty.Register<MainWindow, double>(nameof(NavRailWidth), ExpandedNavWidth);

    /// <summary>当前版本是否提供模型/训练/推理（Lite 版为 false，导航项整组隐藏）。</summary>
    public static readonly StyledProperty<bool> SupportsModelWorkflowProperty =
        AvaloniaProperty.Register<MainWindow, bool>(nameof(SupportsModelWorkflow), true);

    /// <summary>标题栏中部：当前打开的项目名（来自 <see cref="MainWindowViewModel"/>）。</summary>
    public static readonly StyledProperty<string?> CurrentProjectNameProperty =
        AvaloniaProperty.Register<MainWindow, string?>(nameof(CurrentProjectName));

    public static readonly StyledProperty<bool> HasCurrentProjectProperty =
        AvaloniaProperty.Register<MainWindow, bool>(nameof(HasCurrentProject));

    static MainWindow()
    {
        IsNavCollapsedProperty.Changed.AddClassHandler<MainWindow>(
            (window, args) => window.ApplyNavState(args.GetNewValue<bool>()));
    }

    /// <summary>
    /// 无容器构造：设计时与无头渲染直接 <c>new MainWindow()</c>。会尝试用默认启动参数建一个真容器，
    /// 建不起来（或数据目录不可用）时页面退回 stub 上下文，外壳照样画得出来。
    /// </summary>
    public MainWindow()
        : this(DesignTimeComposition.TryCreateServices())
    {
    }

    public MainWindow(IServiceProvider? services)
    {
        _services = services;
        _navigation = Resolve<AvaloniaNavigationService>()
            ?? new AvaloniaNavigationService(services, Resolve<WorkspaceSessionService>());
        _viewModel = Resolve<MainWindowViewModel>();
        _snackbarHost = Resolve<AvaloniaSnackbarHost>();
        _themeService = Resolve<IAppThemeService>();
        _settingsService = Resolve<ShellSettingsService>();

        InitializeComponent();
        ApplyNavState(IsNavCollapsed);

        // 任务中心与 WPF 一致：壳层单例 VM，面板自己画在内容区右下角。
        ShellTaskCenter.DataContext = Resolve<ShellTaskCenterViewModel>();

        // 导航项只在 XAML 里声明，选中即切页：构造期统一挂 IsCheckedChanged，XAML 里的 IsChecked 不会先于内容宿主触发。
        foreach (var button in NavRail.GetLogicalDescendants().OfType<RadioButton>())
        {
            var label = button.Content?.ToString() ?? string.Empty;
            if (RoutesByLabel.TryGetValue(label, out var route))
            {
                _navButtons[route] = button;
            }

            button.IsCheckedChanged += OnNavChecked;
        }

        _navigation.Attach(this);
        _navigation.Navigated += OnNavigatedAppRoute;

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        if (_snackbarHost is not null)
        {
            _snackbarHost.ShowRequested += OnSnackbarRequested;
        }

        if (Resolve<ShellSnackbarNotifier>() is { } snackbarNotifier && _snackbarHost is not null)
        {
            snackbarNotifier.Attach(_snackbarHost);
        }

        _snackbarTimer.Tick += OnSnackbarElapsed;

        // 剪贴板、对话框、焦点探针都要 TopLevel：窗口在这里登记，关闭时清空。
        ShellWindowHost.Window = this;
        Closed += OnClosed;

        _themeService?.Apply(_settingsService?.Settings.Theme ?? ShellThemePreference.System);
        if (_themeService is not null)
        {
            _themeService.Changed += OnThemeChanged;
        }

        UpdateThemeToggleVisual();

        _navigation.Navigate(AppRoute.Dashboard);
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

    bool IPageHost.IsAttached => ContentHost is not null;

    void IPageHost.Show(Control? page, AppRoute route)
        => ContentHost.Content = page ?? CreatePlaceholderPage(route);

    private T? Resolve<T>()
        where T : class
        => _services?.GetService(typeof(T)) as T;

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        if (e.PropertyName is nameof(MainWindowViewModel.CurrentProjectName))
        {
            CurrentProjectName = _viewModel.CurrentProjectName;
        }
        else if (e.PropertyName is nameof(MainWindowViewModel.HasCurrentProject))
        {
            HasCurrentProject = _viewModel.HasCurrentProject;
        }
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

    /// <summary>主题偏好写回设置并应用，与 WPF 版一致（持久化失败不影响本次切换）。</summary>
    private void OnToggleTheme(object? sender, RoutedEventArgs e)
    {
        var preference = IsDarkThemeActive() ? ShellThemePreference.Light : ShellThemePreference.Dark;

        if (_settingsService is not null)
        {
            var settings = _settingsService.Settings;
            settings.Theme = preference;
            try
            {
                _settingsService.Save(settings);
            }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
            {
                System.Diagnostics.Trace.WriteLine($"Failed to persist theme preference: {ex.Message}");
            }
        }

        _themeService?.Apply(preference);
        UpdateThemeToggleVisual();
    }

    private bool IsDarkThemeActive() => _themeService?.Current switch
    {
        ShellThemePreference.Dark => true,
        ShellThemePreference.Light => false,
        _ => global::Avalonia.Application.Current?.ActualThemeVariant == ThemeVariant.Dark
    };

    private void OnThemeChanged(object? sender, EventArgs e) => UpdateThemeToggleVisual();

    /// <summary>
    /// 图标表示「点下去会切到哪一档」：浅色档显示 Sparkles（IvIcons 没有月亮，用星夜顶替），深色档显示 Lamp（没有太阳，用灯顶替）。
    /// </summary>
    private void UpdateThemeToggleVisual()
        => ThemeToggleIcon.Data = IsDarkThemeActive() ? IvIcons.Lamp : IvIcons.Sparkles;

    /// <summary>导航选中即交给导航服务切内容区。</summary>
    private void OnNavChecked(object? sender, RoutedEventArgs e)
    {
        if (_isSyncingNavSelection || sender is not RadioButton { IsChecked: true } button)
        {
            return;
        }

        var label = button.Content?.ToString() ?? string.Empty;
        if (RoutesByLabel.TryGetValue(label, out var route))
        {
            _navigation.Navigate(route);
        }
    }

    /// <summary>服务驱动的导航（侧栏点击、VM 主动跳转）都会走到这里：回写选中态时不能再触发一次导航。</summary>
    private void OnNavigatedAppRoute(object? sender, AppRoute route)
    {
        if (!_navButtons.TryGetValue(route, out var button))
        {
            return;
        }

        _isSyncingNavSelection = true;
        try
        {
            button.IsChecked = true;
        }
        finally
        {
            _isSyncingNavSelection = false;
        }
    }

    /// <summary>还没有 Avalonia 页面的路由：图标取导航项 Tag，文案是「&lt;导航名&gt; · 迁移中」。</summary>
    private ContentControl CreatePlaceholderPage(AppRoute route)
    {
        var label = RoutesByLabel.FirstOrDefault(pair => pair.Value == route).Key ?? route.ToString();
        return new ContentControl
        {
            Theme = this.TryFindResource("PlaceholderPageTheme", out var theme) ? theme as ControlTheme : null,
            Content = $"{label} · 迁移中",
            Tag = _navButtons.TryGetValue(route, out var button) ? button.Tag : null,
        };
    }

    private void OnSnackbarRequested(SnackbarRequest request)
    {
        _snackbarGeneration++;

        SnackbarTitle.Text = request.Title;
        SnackbarTitle.IsVisible = !string.IsNullOrWhiteSpace(request.Title);
        SnackbarMessage.Text = request.Message;
        SnackbarIcon.Data = SnackbarIconFor(request.Kind);
        if (this.TryFindResource(SnackbarBrushKeyFor(request.Kind), ActualThemeVariant, out var brush)
            && brush is IBrush iconBrush)
        {
            SnackbarIcon.Foreground = iconBrush;
        }

        SnackbarLayer.IsVisible = true;
        SnackbarLayer.Opacity = 1;

        // 连续多次调用时：新的直接替换旧的——停掉上一轮计时、重新计时。
        _snackbarTimer.Stop();
        _snackbarTimer.Interval = request.Duration > TimeSpan.Zero ? request.Duration : DefaultSnackbarDuration;
        _snackbarTimer.Start();
    }

    private void OnSnackbarElapsed(object? sender, EventArgs e)
    {
        _snackbarTimer.Stop();
        var generation = _snackbarGeneration;
        Dispatcher.UIThread.Post(
            () =>
            {
                // 淡出期间若来了新提示条，旧的一轮不许把它藏起来。
                if (generation == _snackbarGeneration)
                {
                    SnackbarLayer.IsVisible = false;
                }
            },
            DispatcherPriority.Background);
    }

    private static Geometry SnackbarIconFor(SnackbarKind kind) => kind switch
    {
        SnackbarKind.Success => IvIcons.Success,
        SnackbarKind.Warning => IvIcons.Warning,
        SnackbarKind.Error => IvIcons.Error,
        _ => IvIcons.Info
    };

    private static string SnackbarBrushKeyFor(SnackbarKind kind) => kind switch
    {
        SnackbarKind.Success => "IvSuccessBrush",
        SnackbarKind.Warning => "IvWarningBrush",
        SnackbarKind.Error => "IvDangerBrush",
        _ => "IvInfoBrush"
    };

    private void OnClosed(object? sender, EventArgs e)
    {
        Closed -= OnClosed;
        _snackbarTimer.Stop();

        if (_themeService is not null)
        {
            _themeService.Changed -= OnThemeChanged;
        }

        _navigation.Navigated -= OnNavigatedAppRoute;

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        if (_snackbarHost is not null)
        {
            _snackbarHost.ShowRequested -= OnSnackbarRequested;
        }

        if (ReferenceEquals(ShellWindowHost.Window, this))
        {
            ShellWindowHost.Window = null;
        }
    }
}
