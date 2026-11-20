using DeepVisionSharp.Presentation.Ports;
using Microsoft.Extensions.Logging;

namespace DeepVisionSharp.Presentation.Services;

public sealed class ShellSnackbarNotifier
{
    private readonly ShellSettingsService _settingsService;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly ILogger<ShellSnackbarNotifier> _logger;
    private ISnackbarHost? _snackbarHost;

    public ShellSnackbarNotifier(
        ShellSettingsService settingsService,
        IUiDispatcher uiDispatcher,
        ILogger<ShellSnackbarNotifier> logger)
    {
        _settingsService = settingsService;
        _uiDispatcher = uiDispatcher;
        _logger = logger;
    }

    /// <summary>由 <c>MainWindow</c> 在 <c>InitializeComponent()</c> 之后调用。</summary>
    public void Attach(ISnackbarHost snackbarHost)
    {
        ArgumentNullException.ThrowIfNull(snackbarHost);
        _snackbarHost = snackbarHost;
    }

    public void ShowInfo(string message, string title = "")
    {
        Show(message, title, SnackbarKind.Info);
    }

    public void ShowSuccess(string message, string title = "")
    {
        Show(message, title, SnackbarKind.Success);
    }

    public void ShowWarning(string message, string title = "")
    {
        _logger.LogWarning("Snackbar warning{TitleSuffix}: {Message}", TitleSuffix(title), message);
        Show(message, title, SnackbarKind.Warning);
    }

    public void ShowError(string message, string title = "")
    {
        _logger.LogError("Snackbar error{TitleSuffix}: {Message}", TitleSuffix(title), message);
        Show(message, title, SnackbarKind.Error);
    }

    private static string TitleSuffix(string title)
        => string.IsNullOrWhiteSpace(title) ? string.Empty : $" [{title.Trim()}]";

    private void Show(string message, string title, SnackbarKind kind)
    {
        if (!_settingsService.Settings.ShowNotifications)
        {
            return;
        }

        // 宿主不保证调用方在 UI 线程（后台任务也会发提示），由端口决定是同步还是排队。
        _uiDispatcher.Post(() => ShowCore(message, title, kind));
    }

    private void ShowCore(string message, string title, SnackbarKind kind)
    {
        try
        {
            // 宿主未就绪（窗口还没建好）时静默丢弃，与旧 ISnackbarService 无 Presenter 时的行为一致。
            if (_snackbarHost is { IsAttached: true } host)
            {
                host.Show(title, message, kind, TimeSpan.FromSeconds(3));
            }
        }
        catch
        {
            // 宿主可能还没挂载或正处于关窗过程中：早期启动不允许因为提示条而崩。
        }
    }
}
