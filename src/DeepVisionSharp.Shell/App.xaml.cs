using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Windows;
using System.Windows.Threading;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Shell.Services;
using DeepVisionSharp.Shell.Views.Dialogs;
namespace DeepVisionSharp.Shell;

public partial class App : System.Windows.Application
{
    private static IHost? _host;
    private static bool _exceptionHandlersRegistered;
    private static SingleInstanceGuard? _singleInstanceGuard;
    private static DeepVisionLaunchOptions _launchOptions = DeepVisionLaunchOptions.Default;

    public static DeepVisionLaunchOptions LaunchOptions => _launchOptions;

    public static IHost Host => _host ??= ShellHost.Create(_launchOptions);

    public static IServiceProvider Services => Host.Services;

    /// <summary>Verification entry points provide parsed arguments before WPF raises Startup.</summary>
    public static IReadOnlyList<string>? StartupArgsOverride { get; set; }

    /// <summary>Verification hosts reuse startup/exit and replace only leaf registrations.</summary>
    protected virtual IHost CreateHost(DeepVisionLaunchOptions options) => ShellHost.Create(options);

    protected override async void OnStartup(StartupEventArgs e)
    {
        // Parse before Host is created so data-root / stub dialog DI can apply.
        try
        {
            _launchOptions = DeepVisionLaunchOptions.Parse(StartupArgsOverride ?? e.Args);
        }
        catch (Exception ex)
        {
            // Invalid harness args must not block on MessageBox (CI/FlaUI).
            System.Diagnostics.Trace.WriteLine($"DeepVisionSharp launch options invalid: {ex.Message}");
            if (ContainsUiTestArg(e.Args))
            {
                Shutdown(-1);
                return;
            }

            try
            {
                StartupMessageDialog.Show(ex.Message, kind: StartupMessageKind.Error);
            }
            catch
            {
                // ignore
            }

            Shutdown(-1);
            return;
        }

        // Register before any Host/UI work so early failures are still intercepted.
        ShellRuntimePaths.SelectForProcess(ShellRuntimePaths.FromOptions(_launchOptions));
        RegisterExceptionHandlers();

        // WER LocalDumps (HKCU): hard/native crashes can leave a minidump even when
        // managed UnhandledException handlers never run. Skip under UI-test harness.
        var localDump = _launchOptions.IsUiTest
            ? LocalDumpRegistrationResult.Failed("ui-test", string.Empty, "skipped")
            : LocalDumpRegistration.EnsureEnabled();

        if (!_launchOptions.SkipSingleInstance
            && !SingleInstanceGuard.TryAcquire(out _singleInstanceGuard))
        {
            StartupMessageDialog.ShowAlreadyRunning();
            Shutdown();
            return;
        }

        try
        {
            _host ??= CreateHost(_launchOptions);
            await Host.StartAsync();
            var logger = Services.GetRequiredService<ILogger<App>>();
            logger.LogInformation(
                "DeepVisionSharp WPF shell started (uiTest={IsUiTest}, dataRoot={DataRoot})",
                _launchOptions.IsUiTest,
                Services.GetRequiredService<IDataRootProvider>().DataDirectory);
            if (localDump.Success)
            {
                logger.LogInformation(
                    "WER LocalDumps enabled for {ExeName}: folder={DumpFolder}, type=Mini, count={DumpCount}",
                    localDump.ExeFileName,
                    localDump.DumpFolder,
                    LocalDumpRegistration.DefaultDumpCount);
            }
            else if (!_launchOptions.IsUiTest)
            {
                logger.LogWarning(
                    "WER LocalDumps registration failed for {ExeName}: {Error}. Folder={DumpFolder}",
                    localDump.ExeFileName,
                    localDump.Error,
                    localDump.DumpFolder);
            }

            Services.GetRequiredService<MainWindow>().Show();

            base.OnStartup(e);
        }
        catch (Exception ex)
        {
            // Offer clipboard copy; do not rethrow — exit with a clear message instead of a hard crash.
            // UI-test harness must fail closed without modal prompts.
            var promptUser = !_launchOptions.IsUiTest;
            ReportUnhandledException(ex, "Startup", showMessage: promptUser);
            if (promptUser)
            {
                try
                {
                    StartupMessageDialog.Show(
                        "应用启动失败，详细信息已写入日志。若刚才选择了复制，报错已在剪贴板中。",
                        kind: StartupMessageKind.Error);
                }
                catch
                {
                    // ignore
                }
            }

            Shutdown(-1);
        }
    }

    private static bool ContainsUiTestArg(IReadOnlyList<string> args)
    {
        for (var i = 0; i < args.Count; i++)
        {
            if (string.Equals(args[i], "--ui-test", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Shutdown must be synchronous. WPF closes the dispatcher once OnExit returns, so an
        // `async void` override would let the process exit mid-way through the awaits — host
        // disposal, the single-instance mutex release and the exit code all happen after the first
        // await and might never run. Block on the async work instead, with a hard bound.
        var shutdown = StopHostAsync();
        if (!shutdown.Wait(TimeSpan.FromSeconds(8)))
        {
            System.Diagnostics.Trace.WriteLine("Shutdown did not complete within 8s; exiting anyway");
        }

        _singleInstanceGuard?.Dispose();
        _singleInstanceGuard = null;

        if (ShellExceptionJournal.ReportedCount > 0)
        {
            e.ApplicationExitCode = 70;
        }

        base.OnExit(e);
    }

    private async Task StopHostAsync()
    {
        if (_host is null)
        {
            return;
        }

        ILogger<App>? logger = null;
        try
        {
            logger = _host.Services.GetService<ILogger<App>>();
            logger?.LogInformation("DeepVisionSharp WPF shell stopping");

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
    }

    private static void RegisterExceptionHandlers()
    {
        if (_exceptionHandlersRegistered)
        {
            return;
        }

        _exceptionHandlersRegistered = true;

        // UI-thread exceptions: mark handled so the process keeps running.
        Current.DispatcherUnhandledException += OnDispatcherUnhandledException;
        Current.Dispatcher.UnhandledExceptionFilter += OnDispatcherUnhandledExceptionFilter;

        // Non-UI / finalizer / thread-pool escapes.
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ReportUnhandledException(
            e.Exception,
            "WPF dispatcher",
            showMessage: !_launchOptions.IsUiTest);
        // Critical: without this, WPF shuts down the app on any unhandled UI exception.
        e.Handled = true;
    }

    private static void OnDispatcherUnhandledExceptionFilter(object sender, DispatcherUnhandledExceptionFilterEventArgs e)
    {
        // Request that the exception still be raised to DispatcherUnhandledException
        // (where we mark it Handled) instead of being swallowed silently.
        e.RequestCatch = true;
    }

    private static void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var exception = e.ExceptionObject as Exception
            ?? new InvalidOperationException($"Unhandled non-exception object: {e.ExceptionObject}");

        // Interactive prompt for humans; log-only under --ui-test so FlaUI does not hang.
        ReportUnhandledException(
            exception,
            e.IsTerminating ? "AppDomain (terminating)" : "AppDomain",
            showMessage: !_launchOptions.IsUiTest);
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        ReportUnhandledException(
            e.Exception,
            "TaskScheduler (unobserved)",
            showMessage: !_launchOptions.IsUiTest);
        // Prevent process tear-down from unobserved task exceptions (.NET policy).
        e.SetObserved();
    }

    private static void ReportUnhandledException(Exception exception, string source, bool showMessage)
    {
        try
        {
            if (_host?.Services is { } services)
            {
                services.GetRequiredService<UnhandledExceptionReporter>()
                    .Report(exception, source, showMessage);
                return;
            }
        }
        catch
        {
            // Fall through to static path.
        }

        UnhandledExceptionReporter.ReportStatic(exception, source, promptUser: showMessage);
    }
}
