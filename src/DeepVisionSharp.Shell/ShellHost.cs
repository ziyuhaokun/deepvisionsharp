using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using System.Windows;
using System.Windows.Threading;

using DeepVisionSharp.Application.Galleries;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Bootstrap;
using DeepVisionSharp.Presentation.Logging;
using DeepVisionSharp.Presentation.Ports;
using DeepVisionSharp.Presentation.Services;
using DeepVisionSharp.Presentation.Services.Dialogs;
using DeepVisionSharp.Presentation.Services.Labeling.WriteBack;
using DeepVisionSharp.Presentation.Services.Navigation;
using DeepVisionSharp.Presentation.ViewModels;
using DeepVisionSharp.Presentation.ViewModels.Dashboard;
using DeepVisionSharp.Presentation.ViewModels.Inference;
using DeepVisionSharp.Presentation.ViewModels.Labeling;
using DeepVisionSharp.Presentation.ViewModels.Models;
using DeepVisionSharp.Shell.Logging;
using DeepVisionSharp.Shell.Services;
using DeepVisionSharp.Shell.Services.Dialogs;
using DeepVisionSharp.Shell.Services.Navigation;
using DeepVisionSharp.Shell.Services.Training;
using DeepVisionSharp.Shell.Views.Dialogs;
using DeepVisionSharp.Shell.Views.Dialogs.Contents;
using DeepVisionSharp.Shell.Views.Pages;

namespace DeepVisionSharp.Shell;

/// <summary>Shared product/verification composition. Overrides are registered last, at existing ports.</summary>
public static class ShellHost
{
    public static IHost Create(DeepVisionLaunchOptions options, Action<IServiceCollection>? configureOverrides = null)
    {
        var paths = ShellRuntimePaths.FromOptions(options);
        ShellRuntimePaths.SelectForProcess(paths);
        // One options instance shared by the provider and the retention sweep, so the directory and
        // retention budget can never drift apart.
        var logOptions = new FileLoggerOptions();
        return Microsoft.Extensions.Hosting.Host
            .CreateDefaultBuilder()
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.SetMinimumLevel(LogLevel.Information);
                logging.AddProvider(new FileLoggerProvider(logOptions));
            })
            .ConfigureServices(services =>
            {
                services.AddSingleton(options);
                services.AddSingleton(paths);
                services.AddSingleton(logOptions);
                services.AddHostedService<LogRetentionService>();

                // Must register before AddDeepVision so SqliteDb/ProjectFileStore pick up the override.
                services.AddSingleton<IDataRootProvider>(_ =>
                    new DefaultDataRootProvider(options.DataRoot));

                services.AddDeepVision();
                services.AddLabelingInferenceWriteBack();

                // MainWindow 要 Attach 到具体类型：每组具体类型注册紧跟它的接口别名，两处解析到同一实例
                services.AddSingleton<AppNavigationService>();
                services.AddSingleton<IAppNavigationService>(sp => sp.GetRequiredService<AppNavigationService>());
                services.AddSingleton<DialogCatalog>();
                services.AddSingleton<AppDialogService>();
                services.AddSingleton<IAppDialogService>(sp => sp.GetRequiredService<AppDialogService>());
                services.AddTransient<AddLabelDialogContent>();
                services.AddTransient<ClassificationAutoInferenceDialogContent>();
                services.AddTransient<InferenceDebugDialogContent>();
                services.AddTransient<ProjectLabelStatisticsDialogContent>();
                services.AddTransient<GalleryBatchInferenceDialogContent>();
                services.AddTransient<InferenceImageSourceDialogContent>();
                services.AddTransient<InferenceBatchReportDialogContent>();
                services.AddTransient<ProjectEditorDialogContent>();
                services.AddTransient<GalleryEditorDialogContent>();
                services.AddTransient<ModelEditorDialogContent>();
                services.AddTransient<ConfirmDialogContent>();
                services.AddTransient<TrainingJobRenameDialogContent>();
                services.AddTransient<GalleryTargetSelectionDialogContent>();
                services.AddTransient<GallerySamplePickerDialogContent>();
                services.AddTransient<GallerySplitPlannerDialogContent>();
                services.AddTransient<SettingsDebugAccessDialogContent>();
                services.AddSingleton<IAppThemeService, AppThemeService>();
                services.AddSingleton<IUiDispatcher, WpfUiDispatcher>();
                services.AddSingleton<IUiTimerFactory, WpfUiTimerFactory>();
                services.AddSingleton<ITextInputFocusProbe, WpfTextInputFocusProbe>();
                services.AddSingleton<IClipboardService, WpfClipboardService>();
                services.AddSingleton<IImageSourceFactory, WpfImageSourceFactory>();
                if (options.IsUiTest)
                {
                    services.AddSingleton<ILocalFileDialogService, StubLocalFileDialogService>();
                }
                else
                {
                    services.AddSingleton<ILocalFileDialogService, LocalFileDialogService>();
                }

                services.AddSingleton<IProcessLauncher, ShellProcessLauncher>();
                services.AddSingleton<UnhandledExceptionReporter>();
                services.AddSingleton<ShellSettingsService>();
                services.AddSingleton<ShellSnackbarNotifier>();
                services.AddSingleton<WorkspaceSessionService>();
                services.AddSingleton<ShellTaskCenterViewModel>();

                services.AddTrainingWorkspace();

                services.AddSingleton<MainWindow>();
                services.AddSingleton<MainWindowViewModel>();
                services.AddSingleton<DashboardViewModel>();
                services.AddTransient<GalleryViewModel>();
                services.AddTransient<LabelingViewModel>();
                services.AddTransient<ModelsViewModel>();
                services.AddTransient<InferenceViewModel>();
                services.AddTransient<SettingsViewModel>();
                services.AddSingleton<DashboardPage>();
                services.AddTransient<GalleryPage>();
                services.AddTransient<LabelingPage>();
                services.AddTransient<ModelsPage>();
                services.AddTransient<InferencePage>();
                services.AddTransient<SettingsPage>();
                configureOverrides?.Invoke(services);
            })
            .Build();
    }

}
