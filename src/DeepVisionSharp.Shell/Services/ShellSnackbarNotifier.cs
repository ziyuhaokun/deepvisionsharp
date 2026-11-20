using System.Windows.Threading;
using DeepVisionSharp.Shell.Services.Hosting;
using Microsoft.Extensions.Logging;

namespace DeepVisionSharp.Shell.Services;

public sealed class ShellSnackbarNotifier
{
    private readonly ShellSettingsService _settingsService;
    private readonly ILogger<ShellSnackbarNotifier> _logger;
    private ISnackbarHost? _snackbarHost;

    public ShellSnackbarNotifier(
        ShellSettingsService settingsService,
        ILogger<ShellSnackbarNotifier> logger)
    {
        _settingsService = settingsService;
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

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(DispatcherPriority.Normal, () => ShowCore(message, title, kind));
            return;
        }

        ShowCore(message, title, kind);
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
