using System.Diagnostics;

using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Presentation.Logging;
using DeepVisionSharp.Presentation.Services;
using DeepVisionSharp.Shell.Avalonia.Composition;
using DeepVisionSharp.Shell.Avalonia.Services;
using DeepVisionSharp.Shell.Avalonia.Services.Logging;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DeepVisionSharp.Shell.Avalonia;

/// <summary>
/// 进程启动/退出序列，逐步对齐 WPF Shell 的 <c>App.xaml.cs</c>：
/// 解析启动参数 → 注册全局异常拦截 → 写 WER LocalDumps → 抢单实例 → 建组合根 + 挂文件日志
/// （并在日志之后启动保留清理）→ 显示主窗口；退出时停推理、停 Host、放互斥量。
/// <para>
/// 与 WPF 的差异只有平台能力所致的两处：提示窗是非阻塞窗口（Avalonia 没有 MessageBox 模态框），
/// 失败退出码在 <see cref="Program"/> 收口（Avalonia 不保证 Exit 事件回写 lifetime 返回值）。
/// </para>
/// </summary>
public partial class App : global::Avalonia.Application
{
    private static IHost? _host;
    private static UnhandledExceptionReporter? _reporter;
    private static LogRetentionService? _logRetention;
    private static SingleInstanceGuard? _singleInstanceGuard;
    private static IClassicDesktopStyleApplicationLifetime? _lifetime;
    private static bool _exceptionHandlersRegistered;
    private static DeepVisionLaunchOptions _launchOptions = DeepVisionLaunchOptions.Default;
    // 默认按「无人值守」处理：还没读到参数时宁可不弹窗，也不能把 CI/FlaUI 挂住。
    private static bool _isUnattended = true;

    public static DeepVisionLaunchOptions LaunchOptions => _launchOptions;

    /// <summary>记录过的未处理异常条数；<see cref="Program"/> 用它把退出码顶到 70。</summary>
    public static int ReportedExceptionCount => UnhandledExceptionReporter.ReportedCount;

    /// <summary>启动期失败过（参数非法、容器建不起来等）；<see cref="Program"/> 据此以 -1 退出。</summary>
    public static bool StartupFailed { get; private set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            // 非桌面生命周期（离屏渲染等宿主）：这里不做启动流程，交给宿主自己管容器。
            base.OnFrameworkInitializationCompleted();
            return;
        }

        _lifetime = desktop;
        desktop.Exit += OnDesktopExit;

        var args = desktop.Args ?? [];
        _isUnattended = StartupArguments.ContainsUiTest(args);

        // 先解析参数再建容器：--data-root / --ui-test 决定数据目录与桩对话框，晚了就覆盖不掉。
        try
        {
            _launchOptions = DeepVisionLaunchOptions.Parse(args);
        }
        catch (Exception ex)
        {
            FailStartup(
                "LaunchOptions",
                ex,
                "启动参数非法：" + ex.Message);
            base.OnFrameworkInitializationCompleted();
            return;
        }

        _isUnattended |= _launchOptions.IsUiTest;

        try
        {
            ShellRuntimePaths.SelectForProcess(ShellRuntimePaths.FromOptions(_launchOptions));
        }
        catch (Exception ex)
        {
            FailStartup("ShellRuntimePaths", ex, "启动路径初始化失败：" + ex.Message);
            base.OnFrameworkInitializationCompleted();
            return;
        }

        _isUnattended |= ShellRuntimePaths.Current.IsVerification;

        // 必须在 SelectForProcess 之后构造：LogDirectory 的默认值取自 ShellRuntimePaths.Current。
        var logOptions = new FileLoggerOptions();

        // 任何 Host/UI 工作之前注册，早期失败也能被拦截。
        RegisterExceptionHandlers();

        // WER LocalDumps（HKCU）：原生/硬崩溃即使托管拦截器跑不到，也能留下 minidump。
        // UI 测试模式不写注册表，免得测试进程在机器上留副作用。
        var crashDumps = _launchOptions.IsUiTest
            ? StartupCrashDumpRegistration.Skipped("ui-test")
            : StartupCrashDumpRegistration.EnsureEnabled();

        if (!_launchOptions.SkipSingleInstance && !SingleInstanceGuard.TryAcquire(out _singleInstanceGuard))
        {
            Trace.WriteLine("Another DeepVisionSharp Avalonia instance is already running.");
            const string alreadyRunning = "检测到已有运行中的实例。请切换到已打开的窗口继续使用，无需再次启动。";
            if (_isUnattended)
            {
                ShutdownWithoutNotice(0);
            }
            else
            {
                ShowNoticeThenShutdown(alreadyRunning, StartupNoticeWindow.ForAlreadyRunning, exitCode: 0);
            }

            base.OnFrameworkInitializationCompleted();
            return;
        }

        try
        {
            _host = AppComposition.Create(_launchOptions);

            // 组合根只用控制台/Debug 提供程序；文件日志在这一层接管，与保留服务共用同一份 options。
            var loggerFactory = _host.Services.GetRequiredService<ILoggerFactory>();
            loggerFactory.AddProvider(new FileLoggerProvider(logOptions));
            _reporter = new UnhandledExceptionReporter(
                loggerFactory.CreateLogger<UnhandledExceptionReporter>(),
                logOptions);
            _logRetention = new LogRetentionService(logOptions, loggerFactory.CreateLogger<LogRetentionService>());
            _logRetention.Start();

            _host.Start();

            var logger = loggerFactory.CreateLogger<App>();
            logger.LogInformation(
                "DeepVisionSharp Avalonia shell started (uiTest={IsUiTest}, dataRoot={DataRoot}, desktop={ShutdownMode})",
                _launchOptions.IsUiTest,
                _host.Services.GetRequiredService<IDataRootProvider>().DataDirectory,
                desktop.ShutdownMode);
            LogCrashDumpRegistration(logger, crashDumps);

            desktop.MainWindow = _host.Services.GetRequiredService<MainWindow>();
        }
        catch (Exception ex)
        {
            FailStartup("Startup", ex, "启动失败：" + ex.Message);
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// 启动期失败的统一出口：先落盘（异常日志 + 崩溃报告），再按是否无人值守决定给提示还是直接退出。
    /// 一条路径都不允许静默：日志必定有，人看的提示在交互模式下必定有。
    /// </summary>
    private static void FailStartup(string source, Exception exception, string reason)
    {
        StartupFailed = true;
        ReportUnhandledException(exception, source, showMessage: !_isUnattended);

        // 半初始化的容器不能留着后台线程乱跑。
        StopHostWithTimeout(TimeSpan.FromSeconds(5));

        var message = "启动失败。详细信息已写入日志目录：" + Environment.NewLine
            + ShellRuntimePaths.Current.LogDirectory + Environment.NewLine + Environment.NewLine
            + reason;

        if (_isUnattended)
        {
            Trace.WriteLine($"Startup failed ({source}): {reason}");
            ShutdownWithoutNotice(-1);
            return;
        }

        ShowNoticeThenShutdown(message, () => StartupNoticeWindow.ForStartupFailure(message), exitCode: -1);
    }

    /// <summary>
    /// 用提示窗接管这次启动：窗口显示后等用户关掉它，再以指定退出码退出。
    /// 窗口建不出来时退到系统消息框——提示不能因为主题没加载而消失。
    /// </summary>
    private static void ShowNoticeThenShutdown(string message, Func<StartupNoticeWindow> createNotice, int exitCode)
    {
        if (_lifetime is { } lifetime)
        {
            try
            {
                var notice = createNotice();
                // 显式 Show() 而不是把提示窗赋给 MainWindow：实测赋 MainWindow 时提示窗会在 Start
                // 阶段被立刻关掉，用户什么都看不到。
                // 同时把关闭模式切到 OnExplicitShutdown：这时应用里只有这一个提示窗，
                // 只有「用户关掉它」才该退出，不能因为窗口计数抖动提前退出。
                lifetime.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                notice.Closed += (_, _) => lifetime.TryShutdown(exitCode);
                notice.Show();
                return;
            }
            catch (Exception ex)
            {
                ReportUnhandledException(ex, "StartupNotice", showMessage: false);
            }
        }

        StartupNativeMessageBox.ShowError(message);
        ShutdownWithoutNotice(exitCode);
    }

    /// <summary>
    /// 无人值守/无提示退出。此刻 Avalonia 的消息循环还没开始跑，直接把退出排进 Dispatcher：
    /// 循环一起来就执行，进程不会停在一个没有任何窗口的空壳状态。
    /// </summary>
    private static void ShutdownWithoutNotice(int exitCode)
    {
        if (_lifetime is not { } lifetime)
        {
            return;
        }

        Dispatcher.UIThread.Post(() => lifetime.TryShutdown(exitCode), DispatcherPriority.Send);
    }

    private static void OnDesktopExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        // 与 WPF 的 OnExit 同一约束：这里返回后进程就结束，await 之后的收尾可能永远不执行，
        // 所以同步等，并给一个硬上界。
        StopHostWithTimeout(TimeSpan.FromSeconds(8));

        _logRetention?.Dispose();
        _logRetention = null;

        _singleInstanceGuard?.Dispose();
        _singleInstanceGuard = null;

        if (UnhandledExceptionReporter.ReportedCount > 0)
        {
            // 真正生效的退出码 70 由 Program 依据 App.ReportedExceptionCount 收口；
            // 这里同时写一份，Avalonia 若把 Exit 结果回传给 lifetime 也一致。
            e.ApplicationExitCode = 70;
        }
    }

    private static void StopHostWithTimeout(TimeSpan timeout)
    {
        var stop = StopHostAsync();
        if (!stop.Wait(timeout))
        {
            Trace.WriteLine($"Host stop did not complete within {timeout}; exiting anyway");
        }
    }

    private static async Task StopHostAsync()
    {
        if (_host is null)
        {
            return;
        }

        ILogger<App>? logger = null;
        try
        {
            logger = _host.Services.GetService<ILogger<App>>();
            logger?.LogInformation("DeepVisionSharp Avalonia shell stopping");

            var inferenceShutdown = _host.Services.GetService<IInferenceShutdown>();
            if (inferenceShutdown is not null)
            {
                await inferenceShutdown.StopAndDisposeAsync("Application shutting down").ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to stop inference engine during shutdown");
        }

        try
        {
            await _host.StopAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Host stop failed during shutdown");
        }

        try
        {
            _host.Dispose();
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Host dispose failed during shutdown");
        }

        _host = null;
        _reporter = null;
    }

    private static void LogCrashDumpRegistration(ILogger logger, StartupCrashDumpRegistrationResult result)
    {
        if (result.Success)
        {
            logger.LogInformation(
                "WER LocalDumps enabled for {ExeName}: folder={DumpFolder}, type=Mini",
                result.ExeFileName,
                result.DumpFolder);
            return;
        }

        if (_launchOptions.IsUiTest)
        {
            logger.LogDebug(
                "WER LocalDumps skipped for {ExeName}: {Reason}",
                result.ExeFileName,
                result.Error);
            return;
        }

        logger.LogWarning(
            "WER LocalDumps registration failed for {ExeName}: {Error}. Folder={DumpFolder}",
            result.ExeFileName,
            result.Error,
            result.DumpFolder);
    }

    private static void RegisterExceptionHandlers()
    {
        if (_exceptionHandlersRegistered)
        {
            return;
        }

        _exceptionHandlersRegistered = true;

        // UI 线程：标记 Handled 让进程活下去（否则 Avalonia 会结束应用）。
        Dispatcher.UIThread.UnhandledException += OnDispatcherUnhandledException;
        Dispatcher.UIThread.UnhandledExceptionFilter += OnDispatcherUnhandledExceptionFilter;

        // 非 UI 线程 / 线程池 / finalizer 逃逸出来的异常。
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private static void OnDispatcherUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ReportUnhandledException(e.Exception, "Avalonia dispatcher", showMessage: !_isUnattended);
        e.Handled = true;
    }

    private static void OnDispatcherUnhandledExceptionFilter(object? sender, DispatcherUnhandledExceptionFilterEventArgs e)
    {
        // 让异常继续走到 UnhandledException（在那里标记 Handled），而不是被静默丢弃。
        e.RequestCatch = true;
    }

    private static void OnAppDomainUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        var exception = e.ExceptionObject as Exception
            ?? new InvalidOperationException($"Unhandled non-exception object: {e.ExceptionObject}");

        ReportUnhandledException(
            exception,
            e.IsTerminating ? "AppDomain (terminating)" : "AppDomain",
            showMessage: !_isUnattended);
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        ReportUnhandledException(e.Exception, "TaskScheduler (unobserved)", showMessage: !_isUnattended);
        // .NET 的策略是「未观察的 Task 异常」不能拖垮进程，这里显式确认。
        e.SetObserved();
    }

    private static void ReportUnhandledException(Exception exception, string source, bool showMessage)
    {
        if (_reporter is not null)
        {
            _reporter.Report(exception, source, showMessage);
            return;
        }

        UnhandledExceptionReporter.ReportStatic(exception, source, promptUser: showMessage);
    }
}
