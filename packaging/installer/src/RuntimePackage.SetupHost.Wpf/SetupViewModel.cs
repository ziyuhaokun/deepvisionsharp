using Microsoft.Win32;
using RuntimePackage.Engine.Installation;
using RuntimePackage.Engine.Models;
using RuntimePackage.Engine.Packaging;
using RuntimePackage.Engine.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Wpf.Ui.Controls;
using System.Windows.Input;

namespace RuntimePackage.SetupHost;

public enum SetupPage
{
    Preparing,
    Welcome,
    License,
    Options,
    Installing,
    UninstallConfirm,
    Uninstalling,
    Complete
}

internal sealed class SetupViewModel : INotifyPropertyChanged
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private PackageReadResult? _package;
    private InstallationRecord? _installationRecord;
    private string _installationRecordPath = string.Empty;
    private string _setupExecutablePath = string.Empty;
    private CancellationTokenSource? _installCts;

    // Navigation
    private SetupPage _currentPage = SetupPage.Preparing;
    private bool _isUninstallMode;

    // Preparing
    private string _preparationText = "正在准备安装向导，请稍候…";
    private bool _preparationFailed;

    // Welcome
    private string _productTitle = "安装向导";
    private string _productVersion = string.Empty;
    private string _publisher = string.Empty;
    private string _welcomeText = string.Empty;
    private string _logoPath = string.Empty;
    private ImageSource? _logoImage;
    private string _versionSummary = string.Empty;
    private string _buildDetails = string.Empty;
    private bool _showBuildDetails;

    // License
    private string _licenseText = string.Empty;
    private bool _licenseAccepted;

    // Options
    private string _installDirectory = string.Empty;
    private bool _createStartMenuShortcut = true;
    private bool _createDesktopShortcut;
    private string _diskSpaceInfo = string.Empty;

    // Installing
    private int _installProgress;
    private string _progressText = string.Empty;
    private bool _isInstalling;

    // Complete
    private bool _isInstallSuccess;
    private bool _isInstallFailed;
    private string _completeMessage = string.Empty;
    private string _installedPath = string.Empty;
    private bool _launchOnComplete;
    private bool _openInstallDirectory;
    private string _failureDetail = string.Empty;
    private string _uninstallSummary = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? RequestClose;
    public Func<string, string, Task<bool>>? ConfirmOverwriteAsync { get; set; }

    // Commands
    public ICommand BackCommand { get; }
    public ICommand NextCommand { get; }
    public ICommand InstallCommand { get; }
    public ICommand FinishCommand { get; }
    public ICommand CloseCommand { get; }
    public ICommand BrowseCommand { get; }

    public SetupViewModel()
    {
        BackCommand = new RelayCommand(_ => NavigateBack(), _ => CanGoBack);
        NextCommand = new RelayCommand(_ => NavigateNext(), _ => CanGoNext);
        InstallCommand = new RelayCommand(_ => NavigateNext(), _ => CanGoNext);
        FinishCommand = new RelayCommand(_ => { Finish(); RequestClose?.Invoke(this, EventArgs.Empty); });
        CloseCommand = new RelayCommand(_ => RequestClose?.Invoke(this, EventArgs.Empty), _ => !IsInstalling);
        BrowseCommand = new RelayCommand(_ => BrowseInstallDirectory());

        // Re-evaluate command CanExecute when any property changes
        PropertyChanged += (_, _) =>
        {
            (BackCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (NextCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (InstallCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (CloseCommand as RelayCommand)?.RaiseCanExecuteChanged();
        };
    }

    #region Navigation Properties

    public SetupPage CurrentPage
    {
        get => _currentPage;
        private set
        {
            if (SetProperty(ref _currentPage, value))
            {
                OnPropertyChanged(nameof(CanGoBack));
                OnPropertyChanged(nameof(CanGoNext));
                OnPropertyChanged(nameof(IsNextVisible));
                OnPropertyChanged(nameof(IsInstallVisible));
                OnPropertyChanged(nameof(IsFinishVisible));
                OnPropertyChanged(nameof(IsPreparationRunning));
                OnPropertyChanged(nameof(IsPreparationOverlayVisible));
                OnPropertyChanged(nameof(IsSidebarBrandingVisible));
                OnPropertyChanged(nameof(BackButtonText));
                OnPropertyChanged(nameof(WindowTitle));
                OnPropertyChanged(nameof(CloseButtonText));
                OnPropertyChanged(nameof(CompleteTitle));
                OnPropertyChanged(nameof(ShowPostInstallActions));
            }
        }
    }

    public bool CanGoBack =>
        CurrentPage is not (SetupPage.Preparing or SetupPage.Welcome or SetupPage.Installing or SetupPage.UninstallConfirm or SetupPage.Uninstalling or SetupPage.Complete);

    public bool CanGoNext =>
        CurrentPage switch
        {
            SetupPage.Welcome => _package is not null,
            SetupPage.License => LicenseAccepted,
            SetupPage.Options => !string.IsNullOrWhiteSpace(InstallDirectory),
            SetupPage.UninstallConfirm => _installationRecord is not null,
            _ => false
        };

    public bool IsNextVisible =>
        CurrentPage is SetupPage.Welcome or SetupPage.License or SetupPage.Options or SetupPage.UninstallConfirm;

    public bool IsInstallVisible => CurrentPage == SetupPage.Options;

    public bool IsFinishVisible => CurrentPage == SetupPage.Complete;

    public string BackButtonText => "返回";

    public string WindowTitle =>
        CurrentPage switch
        {
            SetupPage.Preparing => IsUninstallMode ? "准备卸载" : "准备安装",
            SetupPage.Welcome => "安装向导",
            SetupPage.License => "许可协议",
            SetupPage.Options => "安装选项",
            SetupPage.Installing => "正在安装",
            SetupPage.UninstallConfirm => "卸载向导",
            SetupPage.Uninstalling => "正在卸载",
            SetupPage.Complete => CompleteTitle,
            _ => "安装向导"
        };

    public string NextButtonText =>
        CurrentPage switch
        {
            SetupPage.Welcome => "下一步 >",
            SetupPage.License => "下一步 >",
            SetupPage.Options => "安装",
            SetupPage.UninstallConfirm => "卸载",
            _ => "下一步 >"
        };

    public bool IsInstallMode => !_isUninstallMode;

    public bool IsUninstallMode => !IsInstallMode;

    public string StepsTitle => IsUninstallMode ? "卸载步骤" : "安装步骤";

    public string FirstStepText => IsUninstallMode ? "确认卸载" : "欢迎";

    public string ActionStepText => IsUninstallMode ? "卸载" : "安装";

    public string CloseButtonText => CurrentPage is SetupPage.Installing or SetupPage.Uninstalling or SetupPage.Complete ? "关闭" : "取消";

    #endregion

    #region Preparing Properties

    public string PreparationText
    {
        get => _preparationText;
        private set => SetProperty(ref _preparationText, value);
    }

    public bool PreparationFailed
    {
        get => _preparationFailed;
        private set
        {
            if (SetProperty(ref _preparationFailed, value))
            {
                OnPropertyChanged(nameof(IsPreparationRunning));
            }
        }
    }

    public bool IsPreparationRunning => CurrentPage == SetupPage.Preparing && !PreparationFailed;

    public bool IsPreparationOverlayVisible => CurrentPage == SetupPage.Preparing;

    public bool IsSidebarBrandingVisible => CurrentPage != SetupPage.Preparing;

    public string PreparationTitle => IsUninstallMode ? "正在准备卸载向导" : "正在准备安装向导";

    public string PreparationOverlayTitle => IsUninstallMode ? "准备卸载中......" : "准备安装中......";

    #endregion

    #region Welcome Properties

    public string ProductTitle
    {
        get => _productTitle;
        private set => SetProperty(ref _productTitle, value);
    }

    public string ProductVersion
    {
        get => _productVersion;
        private set => SetProperty(ref _productVersion, value);
    }

    public string Publisher
    {
        get => _publisher;
        private set => SetProperty(ref _publisher, value);
    }

    public string WelcomeText
    {
        get => _welcomeText;
        private set => SetProperty(ref _welcomeText, value);
    }

    public string LogoPath
    {
        get => _logoPath;
        private set
        {
            if (SetProperty(ref _logoPath, value))
            {
                OnPropertyChanged(nameof(HasLogo));
                LogoImage = LoadLogoImage(value);
            }
        }
    }

    public ImageSource? LogoImage
    {
        get => _logoImage;
        private set
        {
            if (SetProperty(ref _logoImage, value))
            {
                OnPropertyChanged(nameof(HasLogo));
            }
        }
    }

    public bool HasLogo => LogoImage is not null;

    public string VersionSummary
    {
        get => _versionSummary;
        private set => SetProperty(ref _versionSummary, value);
    }

    public string BuildDetails
    {
        get => _buildDetails;
        private set
        {
            if (SetProperty(ref _buildDetails, value))
            {
                OnPropertyChanged(nameof(HasBuildDetails));
            }
        }
    }

    public bool ShowBuildDetails
    {
        get => _showBuildDetails;
        set
        {
            if (SetProperty(ref _showBuildDetails, value))
            {
                OnPropertyChanged(nameof(BuildDetailsToggleText));
            }
        }
    }

    public bool HasBuildDetails => !string.IsNullOrWhiteSpace(BuildDetails);

    public string BuildDetailsToggleText => ShowBuildDetails ? "收起构建信息" : "查看构建信息";

    #endregion

    #region License Properties

    public string LicenseText
    {
        get => _licenseText;
        private set => SetProperty(ref _licenseText, value);
    }

    public bool LicenseAccepted
    {
        get => _licenseAccepted;
        set
        {
            if (SetProperty(ref _licenseAccepted, value))
            {
                OnPropertyChanged(nameof(CanGoNext));
            }
        }
    }

    public bool HasLicense => !string.IsNullOrWhiteSpace(LicenseText);

    #endregion

    #region Options Properties

    public string InstallDirectory
    {
        get => _installDirectory;
        set
        {
            if (SetProperty(ref _installDirectory, value))
            {
                OnPropertyChanged(nameof(CanGoNext));
                UpdateDiskSpaceInfo();
            }
        }
    }

    public bool CreateStartMenuShortcut
    {
        get => _createStartMenuShortcut;
        set => SetProperty(ref _createStartMenuShortcut, value);
    }

    public bool CreateDesktopShortcut
    {
        get => _createDesktopShortcut;
        set => SetProperty(ref _createDesktopShortcut, value);
    }

    public string DiskSpaceInfo
    {
        get => _diskSpaceInfo;
        private set => SetProperty(ref _diskSpaceInfo, value);
    }

    #endregion

    #region Installing Properties

    public int InstallProgress
    {
        get => _installProgress;
        private set => SetProperty(ref _installProgress, value);
    }

    public string ProgressText
    {
        get => _progressText;
        private set => SetProperty(ref _progressText, value);
    }

    public bool IsInstalling
    {
        get => _isInstalling;
        private set
        {
            if (SetProperty(ref _isInstalling, value))
            {
                OnPropertyChanged(nameof(CanGoBack));
            }
        }
    }

    public ObservableCollection<string> Logs { get; } = new();

    #endregion

    #region Complete Properties

    public bool IsInstallSuccess
    {
        get => _isInstallSuccess;
        private set => SetProperty(ref _isInstallSuccess, value);
    }

    public bool IsInstallFailed
    {
        get => _isInstallFailed;
        private set => SetProperty(ref _isInstallFailed, value);
    }

    public string CompleteMessage
    {
        get => _completeMessage;
        private set => SetProperty(ref _completeMessage, value);
    }

    public string InstalledPath
    {
        get => _installedPath;
        private set => SetProperty(ref _installedPath, value);
    }

    public bool LaunchOnComplete
    {
        get => _launchOnComplete;
        set => SetProperty(ref _launchOnComplete, value);
    }

    public bool OpenInstallDirectory
    {
        get => _openInstallDirectory;
        set => SetProperty(ref _openInstallDirectory, value);
    }

    public string FailureDetail
    {
        get => _failureDetail;
        private set => SetProperty(ref _failureDetail, value);
    }

    public string UninstallSummary
    {
        get => _uninstallSummary;
        private set => SetProperty(ref _uninstallSummary, value);
    }

    public string CompleteTitle
    {
        get
        {
            if (IsUninstallMode)
            {
                return _isInstallSuccess ? "卸载完成" : "卸载失败";
            }

            return _isInstallSuccess ? "安装完成" : "安装失败";
        }
    }

    public bool ShowPostInstallActions => IsInstallMode && _isInstallSuccess;

    #endregion

    #region Navigation Commands

    public void NavigateNext()
    {
        if (IsNavigatingOrBlocked()) return;

        switch (CurrentPage)
        {
            case SetupPage.Welcome:
                CurrentPage = HasLicense ? SetupPage.License : SetupPage.Options;
                break;
            case SetupPage.License:
                if (LicenseAccepted)
                    CurrentPage = SetupPage.Options;
                break;
            case SetupPage.Options:
                _ = InstallAsync();
                break;
            case SetupPage.UninstallConfirm:
                _ = UninstallAsync();
                break;
        }
    }

    public void NavigateBack()
    {
        if (IsNavigatingOrBlocked()) return;

        switch (CurrentPage)
        {
            case SetupPage.License:
                CurrentPage = SetupPage.Welcome;
                break;
            case SetupPage.Options:
                CurrentPage = HasLicense ? SetupPage.License : SetupPage.Welcome;
                break;
        }
    }

    public void BrowseInstallDirectory()
    {
        var dialog = new OpenFolderDialog
        {
            Multiselect = false,
            Title = "选择安装目录"
        };

        if (dialog.ShowDialog() == true)
        {
            InstallDirectory = dialog.FolderName;
        }
    }

    public void Finish()
    {
        if (_isInstallSuccess && IsInstallMode)
        {
            if (LaunchOnComplete)
            {
                TryLaunchEntryPoint();
            }

            if (OpenInstallDirectory && Directory.Exists(InstalledPath))
            {
                try
                {
                    System.Diagnostics.Process.Start("explorer.exe", InstalledPath);
                }
                catch
                {
                    // Best effort
                }
            }
        }
    }

    #endregion

    #region Core Logic

    public async Task LoadBundledPackageAsync(string setupExecutablePath, CancellationToken cancellationToken)
    {
        _setupExecutablePath = setupExecutablePath;
        BeginPreparation();

        string? setupBundleRoot = null;
        PackageReadResult? loadedPackage = null;

        try
        {
            PreparationText = "正在读取安装包，请稍候…";
            AddLog("正在读取内置安装包…");

            var payload = await new SetupBundleReader().ExtractPayloadAsync(setupExecutablePath, cancellationToken);
            setupBundleRoot = payload.ExtractedRoot;
            PreparationText = "正在载入安装信息，请稍候…";
            AddLog("安装包载入完成。");

            loadedPackage = await new PackageReader().OpenAsync(payload.ExtractedPackagePath, cancellationToken);
            PreparationText = "正在校验安装包完整性，请稍候…";
            AddLog("清单读取完成。");

            var validation = await new PackageValidator().ValidateAsync(loadedPackage, cancellationToken);

            if (!validation.IsValid)
            {
                FailPreparation("安装包校验失败，安装向导无法继续。");
                foreach (var error in validation.Errors)
                {
                    AddLog(error);
                }
                return;
            }

            TempDirectoryCleanup.TryDeleteDirectory(setupBundleRoot);
            setupBundleRoot = null;

            _package = loadedPackage;
            loadedPackage = null;
            ProductTitle = _package.Manifest.ProductName;
            ApplyPackageBranding(_package);
            Publisher = _package.Manifest.Publisher;

            // Load license
            var licensePath = Path.Combine(_package.ExtractedRoot, "assets", "license.txt");
            if (File.Exists(licensePath))
            {
                LicenseText = await File.ReadAllTextAsync(licensePath, cancellationToken);
            }

            // Set defaults
            InstallDirectory = Environment.ExpandEnvironmentVariables(
                _package.Manifest.Install.DefaultDirectory);
            CreateStartMenuShortcut = _package.Manifest.Install.StartMenuShortcut;
            CreateDesktopShortcut = _package.Manifest.Install.DesktopShortcut;

            WelcomeText = $"欢迎安装 {_package.Manifest.ProductName}。\n\n此向导将引导您完成安装过程。";
            CurrentPage = SetupPage.Welcome;
            AddLog("安装包校验通过，准备就绪。");
        }
        catch (Exception ex)
        {
            _package = null;
            FailPreparation("读取安装包时发生错误，安装向导无法继续。");
            AddLog($"错误：{ex.Message}");
        }
        finally
        {
            if (loadedPackage is not null)
            {
                TempDirectoryCleanup.TryDeleteDirectory(loadedPackage.ExtractedRoot);
            }

            TempDirectoryCleanup.TryDeleteDirectory(setupBundleRoot);
        }
    }

    public async Task LoadPackageFileAsync(string packagePath, string setupExecutablePath, CancellationToken cancellationToken)
    {
        _setupExecutablePath = setupExecutablePath;
        BeginPreparation();

        PackageReadResult? loadedPackage = null;

        try
        {
            PreparationText = "正在读取安装包，请稍候…";
            AddLog("正在读取安装包文件…");
            loadedPackage = await new PackageReader().OpenAsync(packagePath, cancellationToken);
            PreparationText = "正在校验安装包完整性，请稍候…";
            AddLog("清单读取完成。");

            var validation = await new PackageValidator().ValidateAsync(loadedPackage, cancellationToken);
            if (!validation.IsValid)
            {
                FailPreparation("安装包校验失败，安装向导无法继续。");
                foreach (var error in validation.Errors)
                {
                    AddLog(error);
                }
                return;
            }

            _package = loadedPackage;
            loadedPackage = null;
            ProductTitle = _package.Manifest.ProductName;
            ApplyPackageBranding(_package);
            Publisher = _package.Manifest.Publisher;

            var licensePath = Path.Combine(_package.ExtractedRoot, "assets", "license.txt");
            if (File.Exists(licensePath))
            {
                LicenseText = await File.ReadAllTextAsync(licensePath, cancellationToken);
            }

            InstallDirectory = Environment.ExpandEnvironmentVariables(_package.Manifest.Install.DefaultDirectory);
            CreateStartMenuShortcut = _package.Manifest.Install.StartMenuShortcut;
            CreateDesktopShortcut = _package.Manifest.Install.DesktopShortcut;

            WelcomeText = $"欢迎安装 {_package.Manifest.ProductName}。\n\n此向导将引导您完成安装过程。";
            CurrentPage = SetupPage.Welcome;
            AddLog("安装包校验通过，准备就绪。");
        }
        catch (Exception ex)
        {
            _package = null;
            FailPreparation("读取安装包时发生错误，安装向导无法继续。");
            AddLog($"错误：{ex.Message}");
        }
        finally
        {
            if (loadedPackage is not null)
            {
                TempDirectoryCleanup.TryDeleteDirectory(loadedPackage.ExtractedRoot);
            }
        }
    }

    public async Task LoadUninstallRecordAsync(string installationRecordPath, CancellationToken cancellationToken)
    {
        _isUninstallMode = true;
        _installationRecordPath = installationRecordPath;
        BeginPreparation();
        OnPropertyChanged(nameof(IsInstallMode));
        OnPropertyChanged(nameof(IsUninstallMode));
        OnPropertyChanged(nameof(StepsTitle));
        OnPropertyChanged(nameof(FirstStepText));
        OnPropertyChanged(nameof(ActionStepText));
        OnPropertyChanged(nameof(PreparationTitle));
        OnPropertyChanged(nameof(PreparationOverlayTitle));

        try
        {
            PreparationText = "正在读取安装记录，请稍候…";
            AddLog("读取安装记录。");

            var installationJson = await File.ReadAllTextAsync(installationRecordPath, cancellationToken);
            var record = JsonSerializer.Deserialize<InstallationRecord>(installationJson, JsonOptions)
                ?? throw new InvalidOperationException("无法读取安装记录。");

            ValidateInstallationRecordPath(installationRecordPath, record);

            _installationRecord = record;
            ProductTitle = string.IsNullOrWhiteSpace(record.ProductName)
                ? "算法运行环境"
                : record.ProductName;
            ProductVersion = ExtractDisplayVersion(record.Version);
            VersionSummary = string.IsNullOrWhiteSpace(ProductVersion) ? string.Empty : $"版本 {ProductVersion}";
            Publisher = record.Publisher;
            InstallDirectory = record.InstallDirectory;
            LogoPath = ResolveInstalledLogoPath(record.InstallDirectory);
            UninstallSummary = $"即将从此计算机移除 {ProductTitle}。\n\n卸载后，安装目录和相关快捷方式将被删除。";

            AddLog("卸载向导准备就绪。");
            CurrentPage = SetupPage.UninstallConfirm;
        }
        catch (Exception ex)
        {
            _installationRecord = null;
            FailPreparation("读取安装记录时发生错误，卸载向导无法继续。");
            AddLog($"错误：{ex.Message}");
        }
    }

    private async Task InstallAsync()
    {
        var package = _package ?? throw new InvalidOperationException("尚未加载安装包。");

        CurrentPage = SetupPage.Installing;
        IsInstalling = true;
        InstallProgress = 0;
        ProgressText = "正在准备安装…";
        Logs.Clear();

        var installStarted = false;

        try
        {
            // Step 1: Plan
            ProgressText = "正在规划安装…";
            InstallProgress = 5;
            AddLog("准备安装配置。");

            var plan = new InstallPlanner().CreatePlan(
                package,
                InstallDirectory,
                CreateStartMenuShortcut,
                CreateDesktopShortcut,
                _setupExecutablePath);

            // Step 2: Preflight
            ProgressText = "正在检查安装环境…";
            InstallProgress = 10;
            AddLog("检查系统环境和磁盘空间。");

            _installCts = new CancellationTokenSource();
            var ct = _installCts.Token;

            var preflight = await new InstallPreflight().ValidateAsync(plan, ct);

            if (!preflight.IsValid)
            {
                IsInstalling = false;
                IsInstallFailed = true;
                IsInstallSuccess = false;
                CompleteMessage = "安装前检查未通过：";
                FailureDetail = string.Join("\n", preflight.Errors);
                foreach (var error in preflight.Errors)
                {
                    AddLog(error);
                }
                CurrentPage = SetupPage.Complete;
                return;
            }

            if (preflight.RequiresOverwriteConfirmation)
            {
                var confirmed = await ConfirmOverwriteInstallDirectoryAsync(plan.ResolvedInstallDirectory);
                if (!confirmed)
                {
                    IsInstalling = false;
                    CurrentPage = SetupPage.Options;
                    ProgressText = "已取消安装。";
                    AddLog("已取消覆盖安装。");
                    return;
                }

                plan = new InstallPlanner().CreatePlan(
                    package,
                    InstallDirectory,
                    CreateStartMenuShortcut,
                    CreateDesktopShortcut,
                    _setupExecutablePath,
                    allowOverwriteExistingContents: true);
                AddLog("将覆盖当前目录中的已有安装。");
            }

            AddLog($"目标位置：{plan.ResolvedInstallDirectory}");

            // Step 3: Install
            installStarted = true;
            var isAppPayload = string.Equals(
                package.Manifest.RuntimeType,
                "app",
                StringComparison.OrdinalIgnoreCase);
            var extractProgressText = isAppPayload ? "正在解压应用程序文件…" : "正在解压运行时文件…";
            var extractLogText = isAppPayload ? "释放应用程序文件。" : "释放算法运行环境文件。";
            var extractContinueLog = isAppPayload ? "继续释放应用程序文件。" : "继续释放运行环境文件。";
            var dependencyLog = isAppPayload ? "写入应用程序依赖组件。" : "写入算法依赖组件。";

            ProgressText = extractProgressText;
            InstallProgress = 15;
            AddLog(extractLogText);

            // Run install in background so we can animate progress
            var installTask = Task.Run(
                async () => await new InstallExecutor().InstallAsync(plan, ct), ct);

            // Animate progress while install runs (engine doesn't report granular progress)
            var progressSteps = new[]
            {
                (20, extractProgressText, extractContinueLog),
                (35, "正在写入文件…", "写入核心组件。"),
                (50, "正在写入文件…", dependencyLog),
                (65, "正在创建快捷方式…", "创建快捷方式。"),
                (75, "正在注册卸载程序…", "配置卸载入口。"),
                (85, "正在写入安装记录…", "保存安装信息。"),
                (92, "正在完成安装…", "执行最后的收尾检查。"),
            };

            var progressIndex = 0;
            while (!installTask.IsCompleted)
            {
                await Task.Delay(800, CancellationToken.None);
                if (progressIndex < progressSteps.Length && !installTask.IsCompleted)
                {
                    InstallProgress = progressSteps[progressIndex].Item1;
                    ProgressText = progressSteps[progressIndex].Item2;
                    AddLog(progressSteps[progressIndex].Item3);
                    progressIndex++;
                }
            }

            // Propagate any exceptions from the install task
            await installTask;

            InstallProgress = 100;
            ProgressText = "安装完成！";

            IsInstalling = false;
            IsInstallSuccess = true;
            IsInstallFailed = false;
            InstalledPath = plan.ResolvedInstallDirectory;
            CompleteMessage = $"{package.Manifest.ProductName} 已成功安装到：\n{plan.ResolvedInstallDirectory}";
            AddLog("安装完成。");
            CurrentPage = SetupPage.Complete;
        }
        catch (Exception ex)
        {
            IsInstalling = false;
            IsInstallSuccess = false;
            IsInstallFailed = true;
            CompleteMessage = "安装过程中发生错误。";
            FailureDetail = ex.Message;
            AddLog($"安装失败：{ex.Message}");
            CurrentPage = SetupPage.Complete;
        }
        finally
        {
            if (installStarted)
            {
                if (IsInstallSuccess && !string.IsNullOrWhiteSpace(InstalledPath))
                {
                    LogoPath = ResolveInstalledLogoPath(InstalledPath);
                }

                ReleasePackageTemporaryFiles();
            }
        }
    }

    private async Task UninstallAsync()
    {
        if (_installationRecord is null)
        {
            throw new InvalidOperationException("尚未加载安装记录。");
        }

        CurrentPage = SetupPage.Uninstalling;
        IsInstalling = true;
        InstallProgress = 0;
        ProgressText = "正在准备卸载…";
        Logs.Clear();

        try
        {
            AddLog("准备卸载配置。");
            AddLog($"目标位置：{_installationRecord.InstallDirectory}");

            var progressSteps = new[]
            {
                (15, "正在移除快捷方式…", "移除开始菜单和桌面快捷方式。"),
                (35, "正在移除卸载入口…", "清理系统卸载入口。"),
                (60, "正在删除运行环境文件…", "删除已安装的运行环境文件。"),
                (82, "正在清理安装目录…", "清理安装目录中的剩余内容。"),
                (94, "正在完成卸载…", "执行最后的收尾检查。"),
            };

            var uninstallTask = Task.Run(
                async () => await new UninstallExecutor().UninstallAsync(_installationRecordPath, CancellationToken.None));

            var progressIndex = 0;
            while (!uninstallTask.IsCompleted)
            {
                await Task.Delay(700, CancellationToken.None);
                if (progressIndex < progressSteps.Length && !uninstallTask.IsCompleted)
                {
                    InstallProgress = progressSteps[progressIndex].Item1;
                    ProgressText = progressSteps[progressIndex].Item2;
                    AddLog(progressSteps[progressIndex].Item3);
                    progressIndex++;
                }
            }

            await uninstallTask;

            InstallProgress = 100;
            ProgressText = "卸载完成！";
            IsInstalling = false;
            IsInstallSuccess = true;
            IsInstallFailed = false;
            CompleteMessage = $"{ProductTitle} 已成功从此计算机移除。";
            AddLog("卸载完成。");
            CurrentPage = SetupPage.Complete;
        }
        catch (Exception ex)
        {
            IsInstalling = false;
            IsInstallSuccess = false;
            IsInstallFailed = true;
            CompleteMessage = "卸载过程中发生错误。";
            FailureDetail = ex.Message;
            AddLog($"卸载失败：{ex.Message}");
            CurrentPage = SetupPage.Complete;
        }
    }

    #endregion

    #region Helpers

    private bool IsNavigatingOrBlocked()
    {
        return IsInstalling;
    }

    private void BeginPreparation()
    {
        ReleaseTemporaryFiles();
        _package = null;
        PreparationFailed = false;
        PreparationText = IsUninstallMode ? "正在准备卸载向导，请稍候…" : "正在准备安装向导，请稍候…";
        CurrentPage = SetupPage.Preparing;
    }

    public void ReleaseTemporaryFiles()
    {
        ReleasePackageTemporaryFiles();
    }

    private void ReleasePackageTemporaryFiles()
    {
        if (_package is null)
        {
            return;
        }

        TempDirectoryCleanup.TryDeleteDirectory(_package.ExtractedRoot);
    }

    private void FailPreparation(string message)
    {
        PreparationText = message;
        PreparationFailed = true;
        CurrentPage = SetupPage.Preparing;
    }

    private static void ValidateInstallationRecordPath(string installationRecordPath, InstallationRecord record)
    {
        var canonicalRecordPath = Path.GetFullPath(installationRecordPath);
        var canonicalInstallDirectory = Path.GetFullPath(record.InstallDirectory);
        var expectedRecordPath = Path.GetFullPath(Path.Combine(canonicalInstallDirectory, "installation.json"));

        if (!string.Equals(canonicalRecordPath, expectedRecordPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("安装记录路径与安装目录不匹配。");
        }
    }

    private async Task<bool> ConfirmOverwriteInstallDirectoryAsync(string installDirectory)
    {
        if (ConfirmOverwriteAsync is not null)
        {
            return await ConfirmOverwriteAsync(
                "覆盖安装",
                $"安装目录中已经存在文件或安装记录：\n{installDirectory}\n\n继续安装将先清空该目录中的现有内容，然后重新安装。是否继续？");
        }

        var result = System.Windows.MessageBox.Show(
            $"安装目录中已经存在文件或安装记录：\n{installDirectory}\n\n继续安装将先清空该目录中的现有内容，然后重新安装。是否继续？",
            "覆盖安装",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning,
            System.Windows.MessageBoxResult.No);
        return result == System.Windows.MessageBoxResult.Yes;
    }

    private void UpdateDiskSpaceInfo()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(InstallDirectory)) return;
            var expanded = Environment.ExpandEnvironmentVariables(InstallDirectory);
            var root = Path.GetPathRoot(expanded);
            if (!string.IsNullOrWhiteSpace(root))
            {
                var drive = new DriveInfo(root);
                var freeGb = drive.AvailableFreeSpace / (1024.0 * 1024 * 1024);
                DiskSpaceInfo = $"可用磁盘空间：{freeGb:F1} GB";
            }
        }
        catch
        {
            DiskSpaceInfo = string.Empty;
        }
    }

    private void TryLaunchEntryPoint()
    {
        try
        {
            if (_package?.Manifest.EntryPoints.FirstOrDefault() is { } entry && !string.IsNullOrWhiteSpace(InstalledPath))
            {
                var fullPath = Path.Combine(InstalledPath, entry.RelativePath);
                if (File.Exists(fullPath))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = fullPath,
                        WorkingDirectory = InstalledPath,
                        UseShellExecute = true
                    });
                }
            }
        }
        catch
        {
            // Best effort
        }
    }

    private void AddLog(string message)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            Logs.Add(message);
        });
    }

    private void ApplyPackageBranding(PackageReadResult package)
    {
        ProductVersion = ExtractDisplayVersion(package.Manifest.Version);
        VersionSummary = string.IsNullOrWhiteSpace(ProductVersion) ? string.Empty : $"版本 {ProductVersion}";
        BuildDetails = LoadBuildDetails(package);
        ShowBuildDetails = false;
        LogoPath = ResolveLogoPath(package.ExtractedRoot);
    }

    private static string ResolveLogoPath(string extractedRoot)
    {
        var pngPath = Path.Combine(extractedRoot, "assets", "logo.png");
        if (File.Exists(pngPath))
        {
            return pngPath;
        }

        var icoPath = Path.Combine(extractedRoot, "assets", "logo.ico");
        return File.Exists(icoPath) ? icoPath : string.Empty;
    }

    private static string ResolveInstalledLogoPath(string installDirectory)
    {
        var candidates = new[]
        {
            Path.Combine(installDirectory, "metadata", "logo.png"),
            Path.Combine(installDirectory, "assets", "logo.png")
        };

        return candidates.FirstOrDefault(File.Exists) ?? string.Empty;
    }

    private static ImageSource? LoadLogoImage(string logoPath)
    {
        if (string.IsNullOrWhiteSpace(logoPath) || !File.Exists(logoPath))
        {
            return null;
        }

        try
        {
            using var stream = new FileStream(
                logoPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    private static string LoadBuildDetails(PackageReadResult package)
    {
        var versionFilePath = Path.Combine(package.ExtractedRoot, package.Manifest.Metadata.VersionFile.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(versionFilePath))
        {
            return string.Empty;
        }

        try
        {
            var lines = File.ReadAllLines(versionFilePath)
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Where(line => !line.Equals("SnapVersionInfo", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            return string.Join(Environment.NewLine, lines);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string ExtractDisplayVersion(string rawVersion)
    {
        if (string.IsNullOrWhiteSpace(rawVersion))
        {
            return string.Empty;
        }

        foreach (var line in rawVersion.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line.StartsWith("Software Version:", StringComparison.OrdinalIgnoreCase))
            {
                var value = line["Software Version:".Length..].Trim();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }

        return rawVersion.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? rawVersion.Trim();
    }

    #endregion

    #region INotifyPropertyChanged

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    #endregion
}

internal sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Predicate<object?>? _canExecute;

    public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter) => _execute(parameter);

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
