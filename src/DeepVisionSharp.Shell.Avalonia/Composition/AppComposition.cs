using DeepVisionSharp.Application.Galleries;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Bootstrap;
using DeepVisionSharp.Presentation.Logging;
using DeepVisionSharp.Presentation.Ports;
using DeepVisionSharp.Presentation.Services;
using DeepVisionSharp.Presentation.Services.Dialogs;
using DeepVisionSharp.Presentation.Services.Labeling.WriteBack;
using DeepVisionSharp.Presentation.Services.Navigation;
using DeepVisionSharp.Presentation.Services.Training;
using DeepVisionSharp.Presentation.ViewModels;
using DeepVisionSharp.Presentation.ViewModels.Dashboard;
using DeepVisionSharp.Presentation.ViewModels.Inference;
using DeepVisionSharp.Presentation.ViewModels.Labeling;
using DeepVisionSharp.Presentation.ViewModels.Models;
using DeepVisionSharp.Presentation.ViewModels.Training;
using DeepVisionSharp.Shell.Avalonia.Services;
using DeepVisionSharp.Shell.Avalonia.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DeepVisionSharp.Shell.Avalonia.Composition;

/// <summary>
/// Avalonia Shell 的组合根：业务层注册全部来自框架无关的 <see cref="DependencyInjection.AddDeepVision"/>，
/// 这里只补齐「Avalonia 版端口实现 + 表现层服务 + 页面」。
/// </summary>
public static class AppComposition
{
    public static IHost Create(DeepVisionLaunchOptions options)
    {
        var paths = ShellRuntimePaths.FromOptions(options);
        ShellRuntimePaths.SelectForProcess(paths);

        // 不用 Host.CreateDefaultBuilder：它会给 Windows 目标宿主挂 EventLog 提供程序，
        // 而这个程序集也会被非 -windows TFM 的进程（离屏渲染宿主）加载，那种进程上 EventLog 直接抛异常。
        return new HostBuilder()
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.SetMinimumLevel(LogLevel.Information);
                logging.AddSimpleConsole(options => options.SingleLine = true);
                logging.AddDebug();
            })
            .ConfigureServices(services =>
            {
                services.AddSingleton(paths);

                // 必须在 AddDeepVision 之前注册，SqliteDb / ProjectFileStore 才会取到覆盖值。
                services.AddSingleton<IDataRootProvider>(_ => new DefaultDataRootProvider(options.DataRoot));

                services.AddDeepVision();
                services.AddLabelingInferenceWriteBack();
                services.AddAvaloniaShell(options);
            })
            .Build();
    }

    /// <summary>
    /// Shell 层注册：Avalonia 端口实现、表现层服务、页面 ViewModel / 页面与主窗口。
    /// 首页 Dashboard 单例，其余页面 VM 与页面都是 Transient。
    /// </summary>
    public static IServiceCollection AddAvaloniaShell(this IServiceCollection services, DeepVisionLaunchOptions options)
    {
        services.AddSingleton(options);

        // ---- 端口：Avalonia 实现 ----
        services.AddSingleton<IUiDispatcher, AvaloniaUiDispatcher>();
        services.AddSingleton<IUiTimerFactory, AvaloniaUiTimerFactory>();
        services.AddSingleton<ITextInputFocusProbe, AvaloniaTextInputFocusProbe>();
        services.AddSingleton<IClipboardService, AvaloniaClipboardService>();
        services.AddSingleton<IImageSourceFactory, AvaloniaImageSourceFactory>();
        services.AddSingleton<IAppThemeService, AvaloniaAppThemeService>();
        services.AddSingleton<ISystemResourceMonitor, AvaloniaSystemResourceMonitor>();
        services.AddSingleton<AvaloniaSnackbarHost>();
        services.AddSingleton<ISnackbarHost>(sp => sp.GetRequiredService<AvaloniaSnackbarHost>());
        services.AddSingleton<IAppDialogService, AvaloniaDialogService>();
        if (options.IsUiTest)
        {
            services.AddSingleton<ILocalFileDialogService, StubLocalFileDialogService>();
        }
        else
        {
            services.AddSingleton<ILocalFileDialogService, AvaloniaLocalFileDialogService>();
        }
        services.AddSingleton<IProcessLauncher, AvaloniaProcessLauncher>();

        // ---- 表现层服务 ----
        services.AddSingleton<ShellSettingsService>();
        services.AddSingleton<ShellSnackbarNotifier>();
        services.AddSingleton<WorkspaceSessionService>();
        services.AddSingleton<ShellTaskCenterViewModel>();
        services.AddSingleton<MainWindowViewModel>();

        // 设置页读日志保留预算。文件日志 provider 归本 Shell 的日志接线，两边解析到同一个实例。
        services.AddSingleton(new FileLoggerOptions());

        // ---- 训练工作区 ----
        services.AddTransient<TrainingMetricsPoller>();
        services.AddTransient<TrainingPreviewPoller>();
        services.AddTransient<TrainingResourcePoller>();
        services.AddTransient<TrainingWorkspaceCoordinator>();
        services.AddTransient<TrainingViewModel>();

        // ---- 页面 ViewModel ----
        services.AddSingleton<DashboardViewModel>();
        services.AddTransient<GalleryViewModel>();
        services.AddTransient<LabelingViewModel>();
        services.AddTransient<ModelsViewModel>();
        services.AddTransient<InferenceViewModel>();
        services.AddTransient<SettingsViewModel>();

        // ---- 页面与外壳 ----
        services.AddTransient<DashboardPage>();
        services.AddTransient<GalleryPage>();
        services.AddTransient<LabelingPage>();
        services.AddTransient<ModelsPage>();
        services.AddTransient<TrainingPage>();
        services.AddTransient<InferencePage>();
        services.AddTransient<SettingsPage>();
        services.AddSingleton<AvaloniaNavigationService>();
        services.AddSingleton<IAppNavigationService>(sp => sp.GetRequiredService<AvaloniaNavigationService>());
        services.AddSingleton<MainWindow>();

        return services;
    }
}
