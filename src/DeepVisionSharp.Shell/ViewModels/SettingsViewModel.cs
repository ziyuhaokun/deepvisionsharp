using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.Extensions.Logging;

using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

using DeepVisionSharp.Application.Galleries;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Shell.Logging;
using DeepVisionSharp.Shell.Services;
using DeepVisionSharp.Shell.Services.Dialogs;
using DeepVisionSharp.Shell.Services.Tasks;

namespace DeepVisionSharp.Shell.ViewModels;

public enum SettingsCategory
{
    Appearance,
    Storage,
    About,
    Advanced
}

public sealed partial class SettingsViewModel : ObservableObject
{
    private const string DebugAccessPasswordEnvironmentVariable = "DEEPVISIONSHARP_DEBUG_PASSWORD";

    private readonly ShellSettingsService _settingsService;
    private readonly IProcessLauncher _processLauncher;
    private readonly IAppThemeService _themeService;
    private readonly IDataIntegrityDebugService _dataIntegrityDebugService;
    private readonly IStorageCleanupService _storageCleanup;
    private readonly ShellTaskCenterViewModel _taskCenter;
    private readonly ShellSnackbarNotifier _snackbar;
    private readonly IAppDialogService _appDialogService;
    private readonly IClipboardService _clipboard;
    private readonly FileLoggerOptions _logOptions;
    private readonly ILogger<SettingsViewModel> _logger;
    private readonly ChangeLogService _changeLog;
    private bool _isDebugAccessDialogOpen;

    public SettingsViewModel(
        ShellSettingsService settingsService,
        IProcessLauncher processLauncher,
        IAppThemeService themeService,
        IDataIntegrityDebugService dataIntegrityDebugService,
        IStorageCleanupService storageCleanup,
        ShellTaskCenterViewModel taskCenter,
        ShellSnackbarNotifier snackbar,
        IAppDialogService appDialogService,
        IClipboardService clipboard,
        FileLoggerOptions logOptions,
        ILogger<SettingsViewModel> logger)
    {
        _logOptions = logOptions;
        _settingsService = settingsService;
        _processLauncher = processLauncher;
        _themeService = themeService;
        _dataIntegrityDebugService = dataIntegrityDebugService;
        _storageCleanup = storageCleanup;
        _taskCenter = taskCenter;
        _snackbar = snackbar;
        _appDialogService = appDialogService;
        _clipboard = clipboard;
        _logger = logger;
        _changeLog = new ChangeLogService();

        ThemeOptions.Add(new ThemeOption(ShellThemePreference.System, "自动", "跟随 Windows 应用主题"));
        ThemeOptions.Add(new ThemeOption(ShellThemePreference.Light, "浅色", "适合明亮环境"));
        ThemeOptions.Add(new ThemeOption(ShellThemePreference.Dark, "深色", "适合低光环境"));

        Categories.Add(new SettingsCategoryItem(
            SettingsCategory.Appearance,
            "外观与通知"));
        Categories.Add(new SettingsCategoryItem(
            SettingsCategory.Storage,
            "存储"));
        Categories.Add(new SettingsCategoryItem(
            SettingsCategory.About,
            "关于"));
    }

    public ObservableCollection<SettingsCategoryItem> Categories { get; } = new();

    public ObservableCollection<ThemeOption> ThemeOptions { get; } = new();

    public SettingsDebugAccessDialogState DebugAccessDialog { get; } = new();

    [ObservableProperty]
    private SettingsCategory _selectedCategory = SettingsCategory.Appearance;

    [ObservableProperty]
    private ShellThemePreference _selectedTheme;

    [ObservableProperty]
    private bool _showNotifications = true;

    [ObservableProperty]
    private string _appDataSize = "计算中...";

    [ObservableProperty]
    private string _modelsSize = "计算中...";

    [ObservableProperty]
    private string _logsSize = "计算中...";

    [ObservableProperty]
    private string _crashDumpsSize = "计算中...";

    [ObservableProperty]
    private bool _isCleaningCache;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isRunningDataRepair;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAdvancedSelected))]
    private bool _isDebugUnlocked;

    [ObservableProperty]
    private string _dataRepairSummary = "尚未执行数据检查。";

    public string AppVersion { get; } = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";

    public string RuntimeVersion { get; } = $".NET {Environment.Version}";

    public string OsVersion { get; } = RuntimeInformation.OSDescription;

    /// <summary>用户版变更条目（仓库根 <c>CHANGELOG.md</c>，Markdown 版本节；技术细节见 <c>CHANGELOG.Developer.md</c>）。</summary>
    public IReadOnlyList<ChangeLogEntry> ChangeLogEntries => _changeLog.Entries;

    public ChangeLogEntry? CurrentChangeLogEntry => _changeLog.CurrentEntry;

    public IReadOnlyList<ChangeLogEntry> HistoricalChangeLogEntries => _changeLog.HistoricalEntries;

    public bool HasCurrentChangeLogEntry => CurrentChangeLogEntry is not null;

    public bool HasHistoricalChangeLogEntries => HistoricalChangeLogEntries.Count > 0;

    public bool HasChangeLogEntries => _changeLog.HasEntries;

    public string ChangeLogEmptyHint => HasChangeLogEntries
        ? string.Empty
        : "未找到用户版 CHANGELOG.md 或未能解析版本节。技术细节见仓库 CHANGELOG.Developer.md。";

    public string HistoricalVersionsHint => HasHistoricalChangeLogEntries
        ? $"另有 {HistoricalChangeLogEntries.Count} 个历史版本节点"
        : string.Empty;

    public bool HasCurrentChangeLogReleasedOn =>
        !string.IsNullOrWhiteSpace(CurrentChangeLogEntry?.ReleasedOn);

    public bool HasCurrentChangeLogTags =>
        !string.IsNullOrWhiteSpace(CurrentChangeLogEntry?.Tags);

    public string AppDataDirectory => _settingsService.AppDataDirectory;

    public string ModelsDirectory => _settingsService.ModelsDirectory;

    public string LogsDirectory => _settingsService.LogDirectory;

    /// <summary>Explains the automatic log retention so the log size is not mistaken for unbounded growth.</summary>
    public string LogRetentionText
    {
        get
        {
            if (_logOptions.RetentionDays <= 0)
            {
                return "日志保留：不自动清理";
            }

            return _logOptions.MaxTotalBytes > 0
                ? $"日志保留：{_logOptions.RetentionDays} 天，上限 {FormatSize(_logOptions.MaxTotalBytes)}"
                : $"日志保留：{_logOptions.RetentionDays} 天";
        }
    }

    public string CrashDumpsDirectory => _settingsService.CrashDumpDirectory;

    public bool IsAppearanceSelected => SelectedCategory == SettingsCategory.Appearance;

    public bool IsStorageSelected => SelectedCategory == SettingsCategory.Storage;

    public bool IsAboutSelected => SelectedCategory == SettingsCategory.About;

    public bool IsAdvancedSelected => IsDebugUnlocked && SelectedCategory == SettingsCategory.Advanced;

    public bool IsThemeSystem => SelectedTheme == ShellThemePreference.System;

    public bool IsThemeLight => SelectedTheme == ShellThemePreference.Light;

    public bool IsThemeDark => SelectedTheme == ShellThemePreference.Dark;

    public bool IsNotCleaningCache => !IsCleaningCache;

    public bool IsNotRunningDataRepair => !IsRunningDataRepair;


    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var settings = _settingsService.Settings;
            SelectedTheme = settings.Theme;
            ShowNotifications = settings.ShowNotifications;
            _themeService.Apply(SelectedTheme);
            await RefreshStorageInfoAsync();
            _logger.LogInformation("Settings page loaded");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void SelectCategory(SettingsCategory category)
    {
        if (category == SettingsCategory.Advanced && !IsDebugUnlocked)
        {
            return;
        }

        SelectedCategory = category;
    }

    [RelayCommand]
    private async Task ShowDebugAccessAsync()
    {
        if (IsDebugUnlocked)
        {
            SelectedCategory = SettingsCategory.Advanced;
            return;
        }

        var configuredPassword = Environment.GetEnvironmentVariable(DebugAccessPasswordEnvironmentVariable);
        if (string.IsNullOrEmpty(configuredPassword))
        {
            _snackbar.ShowWarning(
                $"未配置维护访问凭据（环境变量 {DebugAccessPasswordEnvironmentVariable}）。",
                "维护验证");
            return;
        }

        if (_isDebugAccessDialogOpen)
        {
            return;
        }

        _isDebugAccessDialogOpen = true;
        DebugAccessDialog.Reset();

        try
        {
            while (true)
            {
                var result = await _appDialogService.ShowAsync(
                    AppDialogKind.SettingsDebugAccess,
                    DebugAccessDialog,
                    new DialogOptions(
                        PrimaryButtonIsEnabledBindingPath: nameof(SettingsDebugAccessDialogState.HasPassword)));

                if (result != AppDialogResult.Primary)
                {
                    return;
                }

                if (PasswordsMatch(DebugAccessDialog.Password, configuredPassword))
                {
                    IsDebugUnlocked = true;
                    Categories.Add(new SettingsCategoryItem(SettingsCategory.Advanced, "DEBUG"));
                    SelectedCategory = SettingsCategory.Advanced;
                    return;
                }

                DebugAccessDialog.RejectPassword();
            }
        }
        finally
        {
            DebugAccessDialog.Reset();
            _isDebugAccessDialogOpen = false;
        }
    }

    private static bool PasswordsMatch(string suppliedPassword, string configuredPassword)
    {
        var suppliedBytes = Encoding.UTF8.GetBytes(suppliedPassword);
        var configuredBytes = Encoding.UTF8.GetBytes(configuredPassword);
        if (suppliedBytes.Length != configuredBytes.Length)
        {
            CryptographicOperations.FixedTimeEquals(configuredBytes, configuredBytes);
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(suppliedBytes, configuredBytes);
    }

    [RelayCommand]
    private void PersistSettings()
    {
        SaveSettingsCore(showSnackbar: true);
    }

    [RelayCommand]
    private void SelectTheme(ShellThemePreference theme)
    {
        SelectedTheme = theme;
    }

    [RelayCommand]
    private async Task RefreshStorageInfoAsync()
    {
        var appDataDirectory = AppDataDirectory;
        var modelsDirectory = ModelsDirectory;
        var logsDirectory = LogsDirectory;
        var crashDumpsDirectory = CrashDumpsDirectory;

        var sizes = await Task.Run(() => new
        {
            AppData = GetDirectorySize(appDataDirectory),
            Models = GetDirectorySize(modelsDirectory),
            Logs = GetDirectorySize(logsDirectory),
            CrashDumps = GetDirectorySize(crashDumpsDirectory)
        });

        AppDataSize = FormatSize(sizes.AppData);
        ModelsSize = FormatSize(sizes.Models);
        LogsSize = FormatSize(sizes.Logs);
        CrashDumpsSize = FormatSize(sizes.CrashDumps);
    }

    /// <summary>
    /// Starts cache/orphan-run cleanup on a background worker; UI stays interactive via task center.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCleanCache))]
    private void ClearCache()
    {
        if (IsCleaningCache)
        {
            return;
        }

        IsCleaningCache = true;
        var taskId = _taskCenter.StartTask(
            BackgroundTaskKind.StorageCleanup,
            "清理缓存与未引用训练数据");
        _taskCenter.AppendLog(taskId, "已在后台启动清理，可继续使用其它页面。");
        _snackbar.ShowInfo("正在后台清理缓存与未引用训练数据…", "设置");

        ShellAsync.FireAndForget(
            () => RunStorageCleanupAsync(taskId),
            _logger,
            "StorageCleanup",
            ex =>
            {
                IsCleaningCache = false;
                _taskCenter.CompleteTask(taskId, success: false, ex.Message);
                _snackbar.ShowError($"清理失败：{ex.Message}", "设置");
            });
    }

    private async Task RunStorageCleanupAsync(Guid taskId)
    {
        var success = false;
        string? error = null;
        try
        {
            var progress = new Progress<string>(line => _taskCenter.AppendLog(taskId, line));
            var cleanup = await _storageCleanup
                .CleanAsync(progress)
                .ConfigureAwait(true);

            if (cleanup.IsFailure || cleanup.Value is null)
            {
                error = cleanup.Error ?? "清理缓存失败";
                _snackbar.ShowError(error, "设置");
                return;
            }

            var result = cleanup.Value;
            success = true;
            if (result.TotalDeletedEntries > 0)
            {
                var parts = new List<string>();
                if (result.TempCacheEntriesDeleted > 0)
                {
                    parts.Add($"临时缓存 {result.TempCacheEntriesDeleted} 项（{FormatSize(result.TempCacheBytesReclaimed)}）");
                }

                if (result.OrphanTrainingRunsDeleted > 0)
                {
                    parts.Add($"未引用训练目录 {result.OrphanTrainingRunsDeleted} 个（{FormatSize(result.OrphanTrainingRunsBytesReclaimed)}）");
                }

                var summary =
                    $"清理完成：{string.Join("；", parts)}。共释放 {FormatSize(result.TotalBytesReclaimed)}。";
                _taskCenter.AppendLog(taskId, summary);
                _snackbar.ShowSuccess(summary, "设置");
            }
            else
            {
                const string empty = "未发现可清理的缓存或未引用训练数据。";
                _taskCenter.AppendLog(taskId, empty);
                _snackbar.ShowInfo(empty, "设置");
            }

            _logger.LogInformation(
                "Storage cleanup completed. Temp={TempEntries}/{TempBytes}, OrphanRuns={OrphanRuns}/{OrphanBytes}",
                result.TempCacheEntriesDeleted,
                result.TempCacheBytesReclaimed,
                result.OrphanTrainingRunsDeleted,
                result.OrphanTrainingRunsBytesReclaimed);

            await RefreshStorageInfoAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            _logger.LogError(ex, "Cache cleanup failed");
            _snackbar.ShowError("清理缓存失败，详细信息已写入日志。", "设置");
            throw;
        }
        finally
        {
            _taskCenter.CompleteTask(taskId, success, error);
            IsCleaningCache = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRunDataRepair))]
    private async Task CheckAndRepairDataAsync()
    {
        IsRunningDataRepair = true;
        try
        {
            var result = await _dataIntegrityDebugService.CheckAndRepairAsync();
            if (result.IsFailure || result.Value is null)
            {
                var error = result.Error ?? "数据检查修复失败。";
                DataRepairSummary = error;
                _snackbar.ShowError(error, "DEBUG");
                return;
            }

            var summary = result.Value;
            DataRepairSummary =
                $"扫描图库 {summary.GalleriesScanned} 个，样本 {summary.SamplesScanned} 个，发现标注文件 {summary.AnnotationFilesFound} 个；" +
                $"补写 Annotations {summary.AnnotationRowsInserted} 条，更新 Annotations {summary.AnnotationRowsUpdated} 条，" +
                $"修正样本标注状态 {summary.SampleFlagsUpdated} 条，修正图库统计 {summary.GalleryCountsUpdated} 个，" +
                $"重建标签索引 {summary.LabelIndexesRebuilt} 条，错误 {summary.Errors} 条。";
            _snackbar.ShowSuccess("数据检查修复完成。", "DEBUG");
        }
        catch (Exception ex)
        {
            DataRepairSummary = $"数据检查修复失败：{ex.Message}";
            _snackbar.ShowError("数据检查修复失败，详细信息已写入日志。", "DEBUG");
            _logger.LogError(ex, "Data integrity debug repair failed");
        }
        finally
        {
            IsRunningDataRepair = false;
        }
    }

    [RelayCommand]
    private void OpenAppDataFolder()
    {
        OpenFolder(AppDataDirectory);
    }

    [RelayCommand]
    private void OpenModelsFolder()
    {
        OpenFolder(ModelsDirectory);
    }

    [RelayCommand]
    private void OpenLogsFolder()
    {
        OpenFolder(LogsDirectory);
    }

    [RelayCommand]
    private void OpenCrashDumpsFolder()
    {
        OpenFolder(CrashDumpsDirectory);
    }

    [RelayCommand]
    private void CopyText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        try
        {
            _clipboard.SetText(text);
            _snackbar.ShowSuccess("已复制到剪贴板。", "设置");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to copy text to clipboard");
            _snackbar.ShowError("复制失败。", "设置");
        }
    }

    partial void OnSelectedCategoryChanged(SettingsCategory value)
    {
        OnPropertyChanged(nameof(IsAppearanceSelected));
        OnPropertyChanged(nameof(IsStorageSelected));
        OnPropertyChanged(nameof(IsAboutSelected));
        OnPropertyChanged(nameof(IsAdvancedSelected));
    }

    partial void OnSelectedThemeChanged(ShellThemePreference value)
    {
        OnPropertyChanged(nameof(IsThemeSystem));
        OnPropertyChanged(nameof(IsThemeLight));
        OnPropertyChanged(nameof(IsThemeDark));

        if (IsLoading)
        {
            return;
        }

        SaveSettingsCore(showSnackbar: false);
        _themeService.Apply(value);
    }

    partial void OnShowNotificationsChanged(bool value)
    {
        if (!IsLoading)
        {
            SaveSettingsCore(showSnackbar: false);
        }
    }

    partial void OnIsCleaningCacheChanged(bool value)
    {
        OnPropertyChanged(nameof(IsNotCleaningCache));
        ClearCacheCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsRunningDataRepairChanged(bool value)
    {
        OnPropertyChanged(nameof(IsNotRunningDataRepair));
        CheckAndRepairDataCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Persists the settings. Called from <c>ObservableProperty</c> setters, so it must never throw:
    /// a write failure (read-only folder, full disk) would otherwise escape into the binding path
    /// and surface as a global error dialog on every toggle.
    /// </summary>
    private void SaveSettingsCore(bool showSnackbar)
    {
        var settings = _settingsService.Settings;
        settings.Theme = SelectedTheme;
        settings.ShowNotifications = ShowNotifications;

        try
        {
            _settingsService.Save(settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Failed to save settings");
            _snackbar.ShowError("设置无法写入磁盘，本次修改仅在当前运行期间有效。", "设置");
            return;
        }

        if (showSnackbar)
        {
            _snackbar.ShowSuccess("设置已保存。", "设置");
        }

        _logger.LogInformation("Settings saved");
    }

    private bool CanCleanCache()
    {
        return !IsCleaningCache;
    }

    private bool CanRunDataRepair()
    {
        return !IsRunningDataRepair;
    }

    private void OpenFolder(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            if (!_processLauncher.TryOpenFolder(directory))
            {
                _snackbar.ShowError("无法打开文件夹，详细信息已写入日志。", "设置");
                _logger.LogWarning("Failed to open folder {Directory}", directory);
                return;
            }

            _snackbar.ShowSuccess($"已打开：{directory}", "设置");
        }
        catch (Exception ex)
        {
            _snackbar.ShowError("无法打开文件夹，详细信息已写入日志。", "设置");
            _logger.LogError(ex, "Failed to open folder {Directory}", directory);
        }
    }

    private static long GetDirectorySize(string path)
    {
        if (!Directory.Exists(path))
        {
            return 0;
        }

        try
        {
            return new DirectoryInfo(path)
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .Sum(file => file.Length);
        }
        catch
        {
            return 0;
        }
    }

    private static string FormatSize(long bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB", "TB"];
        double len = bytes;
        var order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }

        return $"{len:0.##} {sizes[order]}";
    }

}

public sealed record ThemeOption(ShellThemePreference Value, string Label, string Description);

public sealed record SettingsCategoryItem(SettingsCategory Id, string Title);
