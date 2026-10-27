using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace DeepVisionSharp.Shell.Services;

public sealed class ShellSnackbarNotifier
{
    private readonly ISnackbarService _snackbarService;
    private readonly ShellSettingsService _settingsService;
    private readonly ILogger<ShellSnackbarNotifier> _logger;

    public ShellSnackbarNotifier(
        ISnackbarService snackbarService,
        ShellSettingsService settingsService,
        ILogger<ShellSnackbarNotifier> logger)
    {
        _snackbarService = snackbarService;
        _settingsService = settingsService;
        _logger = logger;
    }

    public void ShowInfo(string message, string title = "")
    {
        Show(message, title, ControlAppearance.Secondary, SymbolRegular.Info24);
    }

    public void ShowSuccess(string message, string title = "")
    {
        Show(message, title, ControlAppearance.Success, SymbolRegular.CheckmarkCircle24);
    }

    public void ShowWarning(string message, string title = "")
    {
        _logger.LogWarning("Snackbar warning{TitleSuffix}: {Message}", TitleSuffix(title), message);
        Show(message, title, ControlAppearance.Caution, SymbolRegular.Warning24);
    }

    public void ShowError(string message, string title = "")
    {
        _logger.LogError("Snackbar error{TitleSuffix}: {Message}", TitleSuffix(title), message);
        Show(message, title, ControlAppearance.Danger, SymbolRegular.ErrorCircle24);
    }

    private static string TitleSuffix(string title)
        => string.IsNullOrWhiteSpace(title) ? string.Empty : $" [{title.Trim()}]";

    private void Show(string message, string title, ControlAppearance appearance, SymbolRegular icon)
    {
        if (!_settingsService.Settings.ShowNotifications)
        {
            return;
        }

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(DispatcherPriority.Normal, () => ShowCore(message, title, appearance, icon));
            return;
        }

        ShowCore(message, title, appearance, icon);
    }

    private void ShowCore(string message, string title, ControlAppearance appearance, SymbolRegular icon)
    {
        try
        {
            _snackbarService.Show(
                title,
                message,
                appearance,
                new SymbolIcon(icon),
                TimeSpan.FromSeconds(3));
        }
        catch
        {
            // Presenter may not be attached during early startup.
        }
    }
}
