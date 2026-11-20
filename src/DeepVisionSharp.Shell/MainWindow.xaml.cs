using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DeepVisionSharp.Application.Training;
using DeepVisionSharp.Shell.Services;
using DeepVisionSharp.Shell.Services.Dialogs;
using DeepVisionSharp.Shell.Services.Hosting;
using DeepVisionSharp.Shell.Services.Navigation;
using DeepVisionSharp.Shell.ViewModels;
using Microsoft.Extensions.Logging;
using YuUI;
using YuUI.Controls;

namespace DeepVisionSharp.Shell;

/// <summary>
/// 壳层窗口，同时是三个宿主：<see cref="IPageHost"/>（<c>PageHost</c> 内容区）、
/// <see cref="ISnackbarHost"/>（<c>SnackbarLayer</c> 右下角提示条）、<see cref="IDialogHost"/>（<c>ShellDialog</c> 对话框）。
/// 契约见 docs/shell-chrome-contract.md §3/§4：取代 NavigationView / SnackbarPresenter / ContentDialogHost。
/// </summary>
public partial class MainWindow : ThemedWindow, IPageHost, ISnackbarHost, IDialogHost
{
    private static readonly TimeSpan DefaultSnackbarDuration = TimeSpan.FromSeconds(3);

    private readonly AppNavigationService _navigationService;
    private readonly ShellSnackbarNotifier _snackbarNotifier;
    private readonly AppDialogService _dialogService;
    private readonly IAppDialogService _appDialogService;
    private readonly ShellSettingsService _settingsService;
    private readonly IAppThemeService _themeService;
    private readonly ITrainingJobLifecycleService _trainingLifecycle;
    private readonly ILogger<MainWindow> _logger;
    private readonly DispatcherTimer _snackbarTimer = new();

    private bool _allowClose;
    private bool _isConfirmingExit;
    private bool _isSyncingNavSelection;
    private bool _isDialogOpen;
    private int _snackbarGeneration;

    public MainWindow(
        MainWindowViewModel viewModel,
        ShellTaskCenterViewModel taskCenterViewModel,
        AppNavigationService navigationService,
        ShellSnackbarNotifier snackbarNotifier,
        AppDialogService dialogService,
        IAppDialogService appDialogService,
        ShellSettingsService settingsService,
        IAppThemeService themeService,
        ITrainingJobLifecycleService trainingLifecycle,
        DeepVisionLaunchOptions launchOptions,
        ILogger<MainWindow> logger)
    {
        DataContext = viewModel;
        _navigationService = navigationService;
        _snackbarNotifier = snackbarNotifier;
        _dialogService = dialogService;
        _appDialogService = appDialogService;
        _settingsService = settingsService;
        _themeService = themeService;
        _trainingLifecycle = trainingLifecycle;
        _logger = logger;

        InitializeComponent();

        _navigationService.Attach(this);
        _snackbarNotifier.Attach(this);
        _dialogService.Attach(this);

        ShellTaskCenter.DataContext = taskCenterViewModel;
        _snackbarTimer.Tick += OnSnackbarElapsed;

        _navigationService.Navigated += OnNavigatedAppRoute;
        _themeService.Changed += OnThemeChanged;

        _themeService.Apply(_settingsService.Settings.Theme);
        UpdateThemeToggleVisual();

        Loaded += OnLoaded;
        Closing += OnClosing;
        Closed += OnClosed;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // 走服务导航：AppNavigationService 导航成功后 raise Navigated，侧栏选中态由 OnNavigatedAppRoute 统一同步，
        // 因此这里不再手工预选导航项，也不会出现「窗口已显示但侧栏无选中」。
        _logger.LogInformation("Navigating to dashboard page");
        _navigationService.Navigate(AppRoute.Dashboard);
    }

    private void OnNavSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSyncingNavSelection)
        {
            return;
        }

        if (NavList.SelectedItem is ListBoxItem { Tag: AppRoute route })
        {
            _navigationService.Navigate(route);
        }
    }

    private void OnNavigatedAppRoute(object? sender, AppRoute route)
    {
        if (FindNavItem(route) is not { } item)
        {
            return;
        }

        // 服务驱动的导航（侧栏点击、VM 主动跳转）都可能走到这里，回写选中态时不能再触发一次导航。
        _isSyncingNavSelection = true;
        try
        {
            NavList.SelectedItem = item;
        }
        finally
        {
            _isSyncingNavSelection = false;
        }
    }

    private ListBoxItem? FindNavItem(AppRoute route)
    {
        foreach (var item in NavList.Items)
        {
            if (item is ListBoxItem { Tag: AppRoute itemRoute } navItem && itemRoute == route)
            {
                return navItem;
            }
        }

        return null;
    }

    private void ToggleThemeMode(object sender, RoutedEventArgs e)
    {
        var preference = IsDarkThemeActive() ? ShellThemePreference.Light : ShellThemePreference.Dark;

        var settings = _settingsService.Settings;
        settings.Theme = preference;

        // The theme is applied even when persisting fails: the user asked for it now, and a
        // read-only settings folder must not make the toggle look broken.
        try
        {
            _settingsService.Save(settings);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Failed to persist theme preference");
        }

        _themeService.Apply(preference);

        _logger.LogInformation("Application theme switched to {Theme}", preference);
        UpdateThemeToggleVisual();
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        UpdateThemeToggleVisual();
    }

    /// <summary>当前实际生效的是不是深色调色板；<see cref="ShellThemePreference.System"/> 由 yu-ui 的调色板状态回答。</summary>
    private bool IsDarkThemeActive()
        => _themeService.Current switch
        {
            ShellThemePreference.Dark => true,
            ShellThemePreference.Light => false,
            _ => IvTheme.Current == IvThemeMode.Dark
        };

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        e.Cancel = true;
        if (_isConfirmingExit)
        {
            return;
        }

        _isConfirmingExit = true;
        try
        {
            var message = _trainingLifecycle.HasActiveRun
                ? "训练正在运行，退出将取消当前训练。确定要退出吗？"
                : "确定要退出 DeepVisionSharp 深度学习平台吗？";
            var result = await _appDialogService.ShowAsync(
                AppDialogKind.Confirm,
                new ConfirmDialogRequest(message),
                new DialogOptions(
                    Title: "确认退出",
                    PrimaryButtonText: "退出",
                    CloseButtonText: "取消",
                    DefaultButton: AppDialogDefaultButton.Close));

            if (result != AppDialogResult.Primary)
            {
                return;
            }

            _allowClose = true;
            _logger.LogInformation("User confirmed application exit");
            Close();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to show exit confirmation dialog");
        }
        finally
        {
            _isConfirmingExit = false;
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _logger.LogInformation("Main window closed");
        _themeService.Changed -= OnThemeChanged;
        _navigationService.Navigated -= OnNavigatedAppRoute;
        _snackbarTimer.Stop();
        Closing -= OnClosing;
        Closed -= OnClosed;
    }

    private void UpdateThemeToggleVisual()
    {
        if (ThemeToggleButton is null || ThemeToggleIcon is null)
        {
            return;
        }

        var isDarkTheme = IsDarkThemeActive();
        // IvIcons 里没有天气/月亮类图标：深色时用 Lamp（切换去浅色），浅色时用 Sparkles（切换去深色）。
        ThemeToggleIcon.Data = isDarkTheme ? IvIcons.Lamp : IvIcons.Sparkles;
        ThemeToggleButton.ToolTip = isDarkTheme ? "切换到浅色模式" : "切换到深色模式";
    }

    bool IPageHost.IsAttached => PageHost is not null;

    object? IPageHost.CurrentPage => PageHost?.Content;

    void IPageHost.Show(object page)
    {
        ArgumentNullException.ThrowIfNull(page);
        // PageHost 是 Frame：页面类型是 Page，只能由 Window/Frame 承载。
        PageHost.Content = page;
    }

    bool ISnackbarHost.IsAttached => SnackbarLayer is not null;

    void ISnackbarHost.Show(string title, string message, SnackbarKind kind, TimeSpan duration)
    {
        _snackbarGeneration++;

        SnackbarTitle.Text = title;
        SnackbarTitle.Visibility = string.IsNullOrWhiteSpace(title) ? Visibility.Collapsed : Visibility.Visible;
        SnackbarMessage.Text = message;
        SnackbarIcon.Data = SnackbarIconFor(kind);
        SnackbarIcon.SetResourceReference(Control.ForegroundProperty, SnackbarBrushKeyFor(kind));

        SnackbarLayer.Visibility = Visibility.Visible;
        SnackbarLayer.BeginAnimation(
            UIElement.OpacityProperty,
            new DoubleAnimation(0d, 1d, MotionDuration("IvMotionTimeSpanFast")));

        // 连续多次调用时：新的直接替换旧的——停掉上一轮的计时、重新计时、重新淡入。
        _snackbarTimer.Stop();
        _snackbarTimer.Interval = duration > TimeSpan.Zero ? duration : DefaultSnackbarDuration;
        _snackbarTimer.Start();
    }

    private void OnSnackbarElapsed(object? sender, EventArgs e)
    {
        _snackbarTimer.Stop();
        FadeOutSnackbar(_snackbarGeneration);
    }

    private void FadeOutSnackbar(int generation)
    {
        var fadeOut = new DoubleAnimation(0d, MotionDuration("IvMotionTimeSpanNormal"));
        fadeOut.Completed += (_, _) =>
        {
            // 淡出期间若来了新提示条，旧动画不许把它藏起来。
            if (generation == _snackbarGeneration)
            {
                SnackbarLayer.Visibility = Visibility.Collapsed;
            }
        };

        SnackbarLayer.BeginAnimation(UIElement.OpacityProperty, fadeOut);
    }

    private static Geometry SnackbarIconFor(SnackbarKind kind)
        => kind switch
        {
            SnackbarKind.Success => IvIcons.Success,
            SnackbarKind.Warning => IvIcons.Warning,
            SnackbarKind.Error => IvIcons.Error,
            _ => IvIcons.Info
        };

    private static string SnackbarBrushKeyFor(SnackbarKind kind)
        => kind switch
        {
            SnackbarKind.Success => "IvSuccessBrush",
            SnackbarKind.Warning => "IvWarningBrush",
            SnackbarKind.Error => "IvDangerBrush",
            _ => "IvInfoBrush"
        };

    private Duration MotionDuration(string key)
        => TryFindResource(key) is TimeSpan timeSpan ? new Duration(timeSpan) : new Duration(TimeSpan.Zero);

    bool IDialogHost.IsAttached => DialogLayer is not null;

    async Task<AppDialogResult> IDialogHost.ShowAsync(Dialog dialog, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        if (DialogLayer is null)
        {
            throw new InvalidOperationException("对话框宿主未挂载");
        }

        // 同一时刻只允许一个：把上一个以 None 结束。
        if (_isDialogOpen)
        {
            CloseDialog(AppDialogResult.None);
        }

        // 把 Title/Content/Footer 搬到窗口自己的 ShellDialog 上，而不是把传进来的 yu:Dialog 塞进 ShellDialog.Content：
        // yu:Dialog 的模板本身就是「遮罩 + 卡片」，直接嵌套会画出两层遮罩两层卡片，按钮与 Esc 也会落到外层。
        var title = dialog.Title;
        var content = dialog.Content;
        var footer = dialog.Footer;
        // 传进来的实例只做数据容器：先解除它对内容元素的挂载，避免搬到 ShellDialog 时撞「已有逻辑父级」。
        dialog.Content = null;

        ShellDialog.Title = title;
        ShellDialog.Content = content;
        ShellDialog.Footer = footer;
        // dv.dialog.host 挂在传进来的实例上，跟着搬到真正显示的 ShellDialog。
        AutomationProperties.SetAutomationId(ShellDialog, AutomationProperties.GetAutomationId(dialog));

        _isDialogOpen = true;
        DialogLayer.Visibility = Visibility.Visible;

        using var registration = cancellationToken.Register(() => CloseDialogFromAnyThread(AppDialogResult.None));
        try
        {
            // 令牌进来前就已取消：此时 ShellDialog 还没 Open，Hide 是 no-op，不能进 await（永远等不到结果）。
            if (cancellationToken.IsCancellationRequested)
            {
                return AppDialogResult.None;
            }

            return MapDialogResult(await ShellDialog.ShowAsync());
        }
        finally
        {
            _isDialogOpen = false;
            DialogLayer.Visibility = Visibility.Collapsed;
            ShellDialog.Title = null;
            ShellDialog.Content = null;
            ShellDialog.Footer = null;
        }
    }

    void IDialogHost.Close(AppDialogResult result) => CloseDialog(result);

    private void CloseDialog(AppDialogResult result)
    {
        if (!_isDialogOpen)
        {
            return;
        }

        ShellDialog.Hide(MapDialogChoice(result));
    }

    private void CloseDialogFromAnyThread(AppDialogResult result)
    {
        if (Dispatcher.CheckAccess())
        {
            CloseDialog(result);
            return;
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => CloseDialog(result)));
    }

    private static DialogChoice MapDialogChoice(AppDialogResult result)
        => result switch
        {
            AppDialogResult.Primary => DialogChoice.Primary,
            AppDialogResult.Secondary => DialogChoice.Secondary,
            _ => DialogChoice.None
        };

    private static AppDialogResult MapDialogResult(DialogChoice choice)
        => choice switch
        {
            DialogChoice.Primary => AppDialogResult.Primary,
            DialogChoice.Secondary => AppDialogResult.Secondary,
            _ => AppDialogResult.None
        };
}
