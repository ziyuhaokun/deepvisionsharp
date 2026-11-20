using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeepVisionSharp.Presentation.Services;

namespace DeepVisionSharp.Shell.Avalonia.ViewModels;

/// <summary>设置分区（对应 deepvision 的 SettingsCategory）。</summary>
public enum SampleSettingsCategory
{
    Appearance,
    Storage,
    About,
    Advanced,
}

/// <summary>
/// 设置页 stub 上下文：绑定路径与 deepvision 的 SettingsViewModel 同名同层，值全部是假数据，
/// 命令只改本对象的属性（不落盘、不打开目录）。
/// </summary>
public sealed class SampleSettingsContext : ObservableObject
{
    private SampleSettingsCategory _selectedCategory = SampleSettingsCategory.Appearance;
    private ShellThemePreference _selectedTheme = ShellThemePreference.Light;
    private bool _showNotifications = true;
    private bool _isDebugUnlocked;

    public SampleSettingsContext()
    {
        Categories =
        [
            new SampleSettingsCategoryItem(SampleSettingsCategory.Appearance, "外观与通知", "Palette"),
            new SampleSettingsCategoryItem(SampleSettingsCategory.Storage, "存储", "Folder"),
            new SampleSettingsCategoryItem(SampleSettingsCategory.About, "关于", "Info"),
            new SampleSettingsCategoryItem(SampleSettingsCategory.Advanced, "高级", "Settings"),
        ];

        SelectThemeCommand = new RelayCommand<ShellThemePreference>(preference =>
        {
            SelectedTheme = preference;
            OnPropertyChanged(nameof(IsThemeSystem));
            OnPropertyChanged(nameof(IsThemeLight));
            OnPropertyChanged(nameof(IsThemeDark));
        });

        ShowDebugAccessCommand = new RelayCommand(() =>
        {
            // 真机走维护密码对话框；stub 直接解锁，好让「高级」分区可以点开看布局。
            IsDebugUnlocked = true;
        });

        OpenAppDataFolderCommand = new RelayCommand(() => { });
        OpenModelsFolderCommand = new RelayCommand(() => { });
        OpenLogsFolderCommand = new RelayCommand(() => { });
        OpenCrashDumpsFolderCommand = new RelayCommand(() => { });
        RefreshStorageInfoCommand = new RelayCommand(() => { });
        ClearCacheCommand = new RelayCommand(() => { });
        CheckAndRepairDataCommand = new RelayCommand(() => { });
        CopyTextCommand = new RelayCommand(() => { });
    }

    public IReadOnlyList<SampleSettingsCategoryItem> Categories { get; }

    public SampleSettingsCategory SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (SetProperty(ref _selectedCategory, value))
            {
                OnPropertyChanged(nameof(IsAppearanceSelected));
                OnPropertyChanged(nameof(IsStorageSelected));
                OnPropertyChanged(nameof(IsAboutSelected));
                OnPropertyChanged(nameof(IsAdvancedSelected));
            }
        }
    }

    public ShellThemePreference SelectedTheme
    {
        get => _selectedTheme;
        set => SetProperty(ref _selectedTheme, value);
    }

    public bool ShowNotifications
    {
        get => _showNotifications;
        set => SetProperty(ref _showNotifications, value);
    }

    public bool IsDebugUnlocked
    {
        get => _isDebugUnlocked;
        set
        {
            if (SetProperty(ref _isDebugUnlocked, value))
            {
                OnPropertyChanged(nameof(IsAdvancedSelected));
            }
        }
    }

    public bool IsAppearanceSelected => SelectedCategory == SampleSettingsCategory.Appearance;

    public bool IsStorageSelected => SelectedCategory == SampleSettingsCategory.Storage;

    public bool IsAboutSelected => SelectedCategory == SampleSettingsCategory.About;

    public bool IsAdvancedSelected => IsDebugUnlocked && SelectedCategory == SampleSettingsCategory.Advanced;

    public bool IsThemeSystem => SelectedTheme == ShellThemePreference.System;

    public bool IsThemeLight => SelectedTheme == ShellThemePreference.Light;

    public bool IsThemeDark => SelectedTheme == ShellThemePreference.Dark;

    public bool IsNotCleaningCache => !IsCleaningCache;

    public bool IsNotRunningDataRepair => !IsRunningDataRepair;

    public string AppDataSize => "1.24 GB";

    public string ModelsSize => "3.86 GB";

    public string LogsSize => "18.4 MB";

    public string CrashDumpsSize => "0 B";

    public bool IsCleaningCache => false;

    public bool IsRunningDataRepair => false;

    public string DataRepairSummary => "上次检查：2025-11-18 14:02，未发现不一致记录。";

    public string AppVersion => "1.0.0";

    public string RuntimeVersion => ".NET 10.0.0";

    public string OsVersion => "Microsoft Windows 10.0.26100 (64 位)";

    public string AppDataDirectory => @"C:\Users\yu\AppData\Local\DeepVisionSharp";

    public string ModelsDirectory => @"C:\Users\yu\AppData\Local\DeepVisionSharp\models";

    public string LogsDirectory => @"C:\Users\yu\AppData\Local\DeepVisionSharp\logs";

    public string CrashDumpsDirectory => @"C:\Users\yu\AppData\Local\DeepVisionSharp\crashes";

    public bool HasCurrentChangeLogEntry => true;

    public SampleChangeLogEntry CurrentChangeLogEntry { get; } = new()
    {
        VersionBadge = "v1.0.0",
        Title = "v1.0.0",
        ReleasedOn = "2025-11-18",
        Tags = "首个正式版",
        Items =
        [
            "设置页迁到 Avalonia 外壳，分区与 WPF 版一致。",
            "新增任务中心浮层与图片灯箱。",
        ],
    };

    public bool HasCurrentChangeLogReleasedOn => true;

    public bool HasCurrentChangeLogTags => true;

    public string ChangeLogEmptyHint => "暂无更新记录。";

    public bool HasHistoricalChangeLogEntries => true;

    public string HistoricalVersionsHint => "更早的版本节点：";

    public IReadOnlyList<SampleChangeLogEntry> HistoricalChangeLogEntries { get; } =
    [
        new()
        {
            VersionBadge = "v0.9.0",
            Title = "v0.9.0 · 2025-10-02",
            Items =
            [
                "增加 AI 判定标准集。",
                "修复批量推理报告漏检率统计。",
            ],
        },
        new()
        {
            VersionBadge = "v0.8.0",
            Title = "v0.8.0 · 2025-09-11",
            Items = ["图库支持按比例拆分。"],
        },
    ];

    public ICommand SelectThemeCommand { get; }

    public ICommand ShowDebugAccessCommand { get; }

    public ICommand OpenAppDataFolderCommand { get; }

    public ICommand OpenModelsFolderCommand { get; }

    public ICommand OpenLogsFolderCommand { get; }

    public ICommand OpenCrashDumpsFolderCommand { get; }

    public ICommand RefreshStorageInfoCommand { get; }

    public ICommand ClearCacheCommand { get; }

    public ICommand CheckAndRepairDataCommand { get; }

    public ICommand CopyTextCommand { get; }
}

/// <summary>设置分区条目：与 SettingsCategoryItem 一样只给图标名，图形由 SettingsCategoryIconConverter 换成 Geometry。</summary>
public sealed record SampleSettingsCategoryItem(SampleSettingsCategory Id, string Title, string Icon);

/// <summary>更新日志条目（对应 ChangeLogEntry）。</summary>
public sealed class SampleChangeLogEntry
{
    public required string VersionBadge { get; init; }

    public required string Title { get; init; }

    public string ReleasedOn { get; init; } = string.Empty;

    public string Tags { get; init; } = string.Empty;

    public required IReadOnlyList<string> Items { get; init; }
}
