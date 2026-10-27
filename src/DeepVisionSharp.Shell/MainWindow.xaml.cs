using System.ComponentModel;
using Microsoft.Extensions.Logging;
using System.Windows;
using DeepVisionSharp.Application.Training;
using DeepVisionSharp.Shell.Services;
using DeepVisionSharp.Shell.Services.Dialogs;
using DeepVisionSharp.Shell.ViewModels;
using DeepVisionSharp.Shell.Views.Pages;
using Wpf.Ui;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace DeepVisionSharp.Shell;

public partial class MainWindow : FluentWindow
{
    private readonly INavigationService _navigationService;
    private readonly ISnackbarService _snackbarService;
    private readonly IContentDialogService _contentDialogService;
    private readonly IAppDialogService _appDialogService;
    private readonly ShellSettingsService _settingsService;
    private readonly IAppThemeService _themeService;
    private readonly ITrainingJobLifecycleService _trainingLifecycle;
    private readonly ILogger<MainWindow> _logger;
    private bool _allowClose;
    private bool _isConfirmingExit;

    public MainWindow(
        MainWindowViewModel viewModel,
        ShellTaskCenterViewModel taskCenterViewModel,
        INavigationService navigationService,
        ISnackbarService snackbarService,
        IContentDialogService contentDialogService,
        IAppDialogService appDialogService,
        ShellSettingsService settingsService,
        IAppThemeService themeService,
        ITrainingJobLifecycleService trainingLifecycle,
        DeepVisionLaunchOptions launchOptions,
        ILogger<MainWindow> logger)
    {
        DataContext = viewModel;
        _navigationService = navigationService;
        _snackbarService = snackbarService;
        _contentDialogService = contentDialogService;
        _appDialogService = appDialogService;
        _settingsService = settingsService;
        _themeService = themeService;
        _trainingLifecycle = trainingLifecycle;
        _logger = logger;

        InitializeComponent();
        ShellTaskCenter.DataContext = taskCenterViewModel;

        _navigationService.SetNavigationControl(RootNavigation);
        _snackbarService.SetSnackbarPresenter(RootSnackbarPresenter);
        _contentDialogService.SetDialogHost(RootContentDialogHost);

        _themeService.Apply(_settingsService.Settings.Theme);

        Loaded += OnLoaded;
        Closing += OnClosing;
        Closed += OnClosed;

        ApplicationThemeManager.Changed += OnApplicationThemeChanged;
        UpdateThemeToggleVisual(ApplicationThemeManager.GetAppTheme());
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // WPF-UI 4.3 NavigationView template part; use the real hamburger, never a goto surrogate.
        if (RootNavigation.Template.FindName("PART_ToggleButton", RootNavigation) is DependencyObject toggle)
        {
            System.Windows.Automation.AutomationProperties.SetAutomationId(toggle, Automation.UiAutomationIds.Nav.Toggle);
        }
        _logger.LogInformation("Navigating to dashboard page");
        _navigationService.Navigate(typeof(DashboardPage));
    }

    private void ToggleThemeMode(object sender, RoutedEventArgs e)
    {
        var preference = ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark
            ? ShellThemePreference.Light
            : ShellThemePreference.Dark;

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
        UpdateThemeToggleVisual(ApplicationThemeManager.GetAppTheme());
    }

    private void OnApplicationThemeChanged(ApplicationTheme applicationTheme, System.Windows.Media.Color systemAccent)
    {
        UpdateThemeToggleVisual(applicationTheme);
    }

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
        ApplicationThemeManager.Changed -= OnApplicationThemeChanged;
        Closing -= OnClosing;
        Closed -= OnClosed;
    }

    private void UpdateThemeToggleVisual(ApplicationTheme applicationTheme)
    {
        if (ThemeToggleButton is null || ThemeToggleIcon is null)
        {
            return;
        }

        bool isDarkTheme = applicationTheme == ApplicationTheme.Dark;
        ThemeToggleIcon.Symbol = isDarkTheme ? SymbolRegular.WeatherSunny24 : SymbolRegular.WeatherMoon24;
        ThemeToggleButton.ToolTip = isDarkTheme ? "切换到浅色模式" : "切换到深色模式";
    }
}
