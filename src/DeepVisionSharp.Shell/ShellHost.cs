using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using System.Windows;
using System.Windows.Threading;

using DeepVisionSharp.Application.Galleries;
using DeepVisionSharp.Bootstrap;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Shell.Logging;
using DeepVisionSharp.Shell.Services;
using DeepVisionSharp.Shell.Services.Dialogs;
using DeepVisionSharp.Shell.Services.Labeling;
using DeepVisionSharp.Shell.Services.Labeling.WriteBack;
using DeepVisionSharp.Shell.Services.Navigation;
using DeepVisionSharp.Shell.Services.Training;
using DeepVisionSharp.Shell.ViewModels;
using DeepVisionSharp.Shell.ViewModels.Dashboard;
using DeepVisionSharp.Shell.ViewModels.Labeling;
using DeepVisionSharp.Shell.ViewModels.Models;
using DeepVisionSharp.Shell.ViewModels.Inference;
using DeepVisionSharp.Shell.ViewModels.Training;
using DeepVisionSharp.Shell.Views.Dialogs;
using DeepVisionSharp.Shell.Views.Dialogs.Contents;
using DeepVisionSharp.Shell.Views.Pages;

using Wpf.Ui;
using Wpf.Ui.DependencyInjection;

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

                services.AddNavigationViewPageProvider();
                services.AddSingleton<INavigationService, NavigationService>();
                services.AddSingleton<IAppNavigationService, AppNavigationService>();
                services.AddSingleton<ISnackbarService, SnackbarService>();
                services.AddSingleton<IContentDialogService, ContentDialogService>();
                services.AddSingleton<DialogCatalog>();
                services.AddSingleton<IAppDialogService, AppDialogService>();
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
                services.AddSingleton<IThemeService, ThemeService>();
                services.AddSingleton<IAppThemeService, WpfAppThemeService>();
                services.AddSingleton<IUiDispatcher, WpfUiDispatcher>();
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
