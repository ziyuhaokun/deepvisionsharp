using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using DeepVisionSharp.Shell.Services.Hosting;
using Microsoft.Extensions.DependencyInjection;
using DeepVisionSharp.UI.Controls;
using WpfBinding = System.Windows.Data.Binding;
using WpfBindingMode = System.Windows.Data.BindingMode;
using WpfButton = System.Windows.Controls.Button;
using DeepVisionSharp.Presentation.Services.Dialogs;
using DeepVisionSharp.Presentation.Automation;

namespace DeepVisionSharp.Shell.Services.Dialogs;

public sealed class AppDialogService : IAppDialogService
{
    private readonly DialogCatalog _catalog;
    private readonly IServiceProvider _services;
    private IDialogHost? _dialogHost;

    public AppDialogService(DialogCatalog catalog, IServiceProvider services)
    {
        _catalog = catalog;
        _services = services;
    }

    /// <summary>
    /// 由 <c>MainWindow</c> 在 <c>InitializeComponent()</c> 之后调用。
    /// 宿主不进构造函数：MainWindow 本身就在 DI 图里，构造期互相持有会撞循环依赖。
    /// </summary>
    public void Attach(IDialogHost dialogHost)
    {
        ArgumentNullException.ThrowIfNull(dialogHost);
        _dialogHost = dialogHost;
    }

    public async Task<AppDialogResult> ShowAsync(
        AppDialogKind kind,
        object dataContext,
        DialogOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var dialogHost = _dialogHost
            ?? throw new InvalidOperationException("对话框宿主未挂载");

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

        var dialog = new Dialog
        {
            Title = options?.Title ?? descriptor.Title,
            Content = content,
        };
        AutomationProperties.SetAutomationId(dialog, UiAutomationIds.Dialog.Host);

        WpfButton? secondaryButton = null;
        WpfButton? closeButton = null;
        var footer = new StackPanel { Orientation = Orientation.Horizontal };
        var gap = dialog.TryFindResource("IvSpace2") is double space ? space : 8d;

        void Append(WpfButton button)
        {
            button.Margin = footer.Children.Count == 0 ? new Thickness(0) : new Thickness(gap, 0, 0, 0);
            footer.Children.Add(button);
        }

        var primaryButton = CreateDialogButton(
            dialog,
            options?.PrimaryButtonText ?? descriptor.PrimaryButtonText,
            primary: true,
            AppDialogResult.Primary,
            UiAutomationIds.Dialog.Primary);
        Append(primaryButton);

        if (!string.IsNullOrWhiteSpace(secondaryButtonText))
        {
            secondaryButton = CreateDialogButton(
                dialog,
                secondaryButtonText,
                primary: false,
                AppDialogResult.Secondary,
                UiAutomationIds.Dialog.Secondary);
            Append(secondaryButton);
        }

        if (!string.IsNullOrWhiteSpace(closeButtonText))
        {
            closeButton = CreateDialogButton(
                dialog,
                closeButtonText,
                primary: false,
                AppDialogResult.None,
                UiAutomationIds.Dialog.Close);
            Append(closeButton);
        }

        dialog.Footer = footer;

        // 原 ContentDialog.DefaultButton 的语义：回车落到默认按钮上。
        (MapDefaultButton(options?.DefaultButton ?? AppDialogDefaultButton.Primary) switch
        {
            AppDialogResult.Secondary => secondaryButton,
            AppDialogResult.None => closeButton,
            _ => primaryButton
        } ?? primaryButton).IsDefault = true;

        if (!string.IsNullOrWhiteSpace(options?.PrimaryButtonIsEnabledBindingPath))
        {
            primaryButton.SetBinding(
                UIElement.IsEnabledProperty,
                new WpfBinding(options.PrimaryButtonIsEnabledBindingPath)
                {
                    Source = dataContext,
                    Mode = WpfBindingMode.OneWay
                });
        }

        return await dialogHost.ShowAsync(dialog, cancellationToken);
    }

    private WpfButton CreateDialogButton(
        Dialog dialog,
        string text,
        bool primary,
        AppDialogResult result,
        string automationId)
    {
        var button = new WpfButton { Content = text };
        if (primary)
        {
            button.SetResourceReference(FrameworkElement.StyleProperty, "IvPrimaryButtonStyle");
        }

        AutomationProperties.SetAutomationId(button, automationId);
        button.Click += (_, _) =>
        {
            // 宿主负责回传结果并关闭对话框；这里再 Hide 一次兜底，
            // 保证「点按钮 → 遮罩消失」不依赖宿主实现细节（Dialog.Hide 已关闭时是无害的 no-op）。
            _dialogHost?.Close(result);
            dialog.Hide(MapDialogChoice(result));
        };
        return button;
    }

    private static AppDialogResult MapDefaultButton(AppDialogDefaultButton button)
        => button switch
        {
            AppDialogDefaultButton.Secondary => AppDialogResult.Secondary,
            AppDialogDefaultButton.Close => AppDialogResult.None,
            _ => AppDialogResult.Primary
        };

    private static DialogChoice MapDialogChoice(AppDialogResult result)
        => result switch
        {
            AppDialogResult.Primary => DialogChoice.Primary,
            AppDialogResult.Secondary => DialogChoice.Secondary,
            _ => DialogChoice.None
        };
}
