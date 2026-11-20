using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.Threading;
using DeepVisionSharp.Presentation.Services.Dialogs;
using YuUI.Controls;

namespace DeepVisionSharp.Shell.Avalonia.Services;

/// <summary>
/// 对话框宿主：把内容控件塞进主窗口的 <c>RootDialog</c>，生成底部按钮并回传结果。
/// 行为对齐 WPF 版 <c>AppDialogService</c>：按钮顺序 关闭/次要/主要；主按钮可绑定启用条件；
/// Esc 与关闭按钮都返回 None；取消令牌触发时以 None 关闭。
/// </summary>
public static class AvaloniaDialogHost
{
    private const double FooterButtonMinWidth = 84;

    public static async Task<AppDialogResult> ShowAsync(
        Window owner,
        DialogOptions options,
        Control content,
        object? dataContext,
        CancellationToken cancellationToken = default)
    {
        var dialog = owner.FindControl<Dialog>("RootDialog")
            ?? throw new InvalidOperationException("主窗口缺少 RootDialog。");

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

        Button? close = null;
        Button? primary = null;

        if (!string.IsNullOrEmpty(options.CloseButtonText))
        {
            close = CreateButton(options.CloseButtonText, DialogChoice.None, themeKey: null);
            buttons.Children.Add(close);
        }

        if (!string.IsNullOrWhiteSpace(options.SecondaryButtonText))
        {
            buttons.Children.Add(CreateButton(options.SecondaryButtonText, DialogChoice.Secondary, themeKey: null));
        }

        if (!string.IsNullOrEmpty(options.PrimaryButtonText))
        {
            primary = CreateButton(options.PrimaryButtonText, DialogChoice.Primary, "IvPrimaryButtonStyle");

            if (!string.IsNullOrWhiteSpace(options.PrimaryButtonIsEnabledBindingPath))
            {
                primary.Bind(
                    InputElement.IsEnabledProperty,
                    new ReflectionBinding(options.PrimaryButtonIsEnabledBindingPath)
                    {
                        Source = dataContext,
                        Mode = BindingMode.OneWay,
                        FallbackValue = false,
                    });
            }

            buttons.Children.Add(primary);
        }

        var closeIsDefault = close is not null && options.DefaultButton == AppDialogDefaultButton.Close;
        if (primary is not null)
        {
            primary.IsDefault = !closeIsDefault;
        }

        if (close is not null)
        {
            close.IsDefault = closeIsDefault;
        }

        dialog.Title = options.Title ?? string.Empty;
        dialog.Content = content;
        dialog.Footer = buttons;

        var shown = dialog.ShowAsync();

        Dispatcher.UIThread.Post(
            () =>
            {
                if (!ReferenceEquals(dialog.Content, content))
                {
                    return;
                }

                var focusTarget = closeIsDefault ? close : primary ?? close;
                if (focusTarget is { IsEffectivelyEnabled: true })
                {
                    focusTarget.Focus();
                }
            },
            DispatcherPriority.Input);

        await using var registration = cancellationToken.Register(
            () => Dispatcher.UIThread.Post(() =>
            {
                if (ReferenceEquals(dialog.Content, content))
                {
                    dialog.Hide(DialogChoice.None);
                }
            }));

        var choice = await shown.ConfigureAwait(true);

        if (ReferenceEquals(dialog.Content, content))
        {
            dialog.Content = null;
            dialog.Footer = null;
        }

        return choice switch
        {
            DialogChoice.Primary => AppDialogResult.Primary,
            DialogChoice.Secondary => AppDialogResult.Secondary,
            _ => AppDialogResult.None,
        };

        Button CreateButton(string text, DialogChoice choice, string? themeKey)
        {
            var button = new Button
            {
                Content = text,
                MinWidth = FooterButtonMinWidth,
            };

            if (themeKey is not null
                && owner.TryFindResource(themeKey, owner.ActualThemeVariant, out var theme)
                && theme is ControlTheme controlTheme)
            {
                button.Theme = controlTheme;
            }

            button.Click += (_, _) => dialog.Hide(choice);
            return button;
        }
    }
}
