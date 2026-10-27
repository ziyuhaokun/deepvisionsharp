using System.Windows;
using System.Windows.Media;
using WpfBinding = System.Windows.Data.Binding;
using WpfBindingMode = System.Windows.Data.BindingMode;
using Microsoft.Extensions.DependencyInjection;
using DeepVisionSharp.Shell.Automation;
using Wpf.Ui;
using Wpf.Ui.Controls;
using WpfButton = System.Windows.Controls.Button;

namespace DeepVisionSharp.Shell.Services.Dialogs;

public sealed class AppDialogService : IAppDialogService
{
    private readonly IContentDialogService _contentDialogService;
    private readonly DialogCatalog _catalog;
    private readonly IServiceProvider _services;

    public AppDialogService(
        IContentDialogService contentDialogService,
        DialogCatalog catalog,
        IServiceProvider services)
    {
        _contentDialogService = contentDialogService;
        _catalog = catalog;
        _services = services;
    }

    public async Task<AppDialogResult> ShowAsync(
        AppDialogKind kind,
        object dataContext,
        DialogOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var descriptor = _catalog.Get(kind);
        var content = (FrameworkElement)_services.GetRequiredService(descriptor.ContentViewType);
        content.DataContext = dataContext;
        var closeButtonText = options?.CloseButtonText ?? descriptor.CloseButtonText;
        // CloseButtonText="" is treated as an explicit "do not show close label" contract
        // for some dialogs; only null should fall back to the default label.
        if (closeButtonText is null)
        {
            closeButtonText = "取消";
        }

        var secondaryButtonText = options?.SecondaryButtonText ?? descriptor.SecondaryButtonText;
        var dialog = new ContentDialog
        {
            Title = options?.Title ?? descriptor.Title,
            Content = content,
            PrimaryButtonText = options?.PrimaryButtonText ?? descriptor.PrimaryButtonText,
            CloseButtonText = closeButtonText,
            DefaultButton = MapDefaultButton(options?.DefaultButton ?? AppDialogDefaultButton.Primary),
        };
        System.Windows.Automation.AutomationProperties.SetAutomationId(dialog, UiAutomationIds.Dialog.Host);
        if (!string.IsNullOrWhiteSpace(secondaryButtonText))
        {
            dialog.SecondaryButtonText = secondaryButtonText;
        }

        if (!string.IsNullOrWhiteSpace(options?.PrimaryButtonIsEnabledBindingPath))
        {
            dialog.SetBinding(
                ContentDialog.IsPrimaryButtonEnabledProperty,
                new WpfBinding(options.PrimaryButtonIsEnabledBindingPath)
                {
                    Source = dataContext,
                    Mode = WpfBindingMode.OneWay
                });
        }

        // Template buttons exist only after load; also handle already-loaded recycled dialogs.
        if (dialog.IsLoaded)
        {
            ApplyFooterButtonAutomationIds(dialog);
        }
        else
        {
            dialog.Loaded += OnDialogLoaded;
        }

        var result = await _contentDialogService.ShowAsync(dialog, cancellationToken);
        return MapResult(result);
    }

    private static void OnDialogLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ContentDialog dialog)
        {
            return;
        }

        dialog.Loaded -= OnDialogLoaded;
        ApplyFooterButtonAutomationIds(dialog);
    }

    private static void ApplyFooterButtonAutomationIds(ContentDialog dialog)
    {
        foreach (var button in EnumerateButtons(dialog))
        {
            var label = ReadButtonLabel(button);
            if (string.IsNullOrWhiteSpace(label))
            {
                continue;
            }

            if (string.Equals(label, dialog.PrimaryButtonText, StringComparison.Ordinal))
            {
                System.Windows.Automation.AutomationProperties.SetAutomationId(
                    button,
                    UiAutomationIds.Dialog.Primary);
            }
            else if (!string.IsNullOrWhiteSpace(dialog.SecondaryButtonText)
                     && string.Equals(label, dialog.SecondaryButtonText, StringComparison.Ordinal))
            {
                System.Windows.Automation.AutomationProperties.SetAutomationId(
                    button,
                    UiAutomationIds.Dialog.Secondary);
            }
            else if (string.Equals(label, dialog.CloseButtonText, StringComparison.Ordinal))
            {
                System.Windows.Automation.AutomationProperties.SetAutomationId(
                    button,
                    UiAutomationIds.Dialog.Close);
            }
        }
    }

    private static IEnumerable<WpfButton> EnumerateButtons(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is WpfButton button)
            {
                yield return button;
            }

            foreach (var nested in EnumerateButtons(child))
            {
                yield return nested;
            }
        }
    }

    private static string? ReadButtonLabel(WpfButton button)
    {
        return button.Content switch
        {
            string text => text,
            System.Windows.Controls.TextBlock textBlock => textBlock.Text,
            _ => button.Content?.ToString()
        };
    }

    private static ContentDialogButton MapDefaultButton(AppDialogDefaultButton button)
        => button switch
        {
            AppDialogDefaultButton.Primary => ContentDialogButton.Primary,
            AppDialogDefaultButton.Secondary => ContentDialogButton.Secondary,
            AppDialogDefaultButton.Close => ContentDialogButton.Close,
            _ => ContentDialogButton.Primary
        };

    private static AppDialogResult MapResult(ContentDialogResult result)
        => result switch
        {
            ContentDialogResult.Primary => AppDialogResult.Primary,
            ContentDialogResult.Secondary => AppDialogResult.Secondary,
            ContentDialogResult.None => AppDialogResult.None,
            _ => AppDialogResult.None
        };
}
