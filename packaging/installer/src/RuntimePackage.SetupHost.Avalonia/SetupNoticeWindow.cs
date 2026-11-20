using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace RuntimePackage.SetupHost;

internal sealed class SetupNoticeWindow : Window
{
    public SetupNoticeWindow(string title, string message, string? confirmText = null)
    {
        Title = title;
        Width = 460;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = confirmText is null;
        WindowStartupLocation = confirmText is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;

        var dismiss = new Button
        {
            Content = confirmText is null ? "确定" : "取消",
            IsDefault = true,
            IsCancel = true,
            MinWidth = 86,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        dismiss.Click += (_, _) => Close(false);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        if (confirmText is not null)
        {
            var confirm = new Button { Content = confirmText, Classes = { "accent" } };
            confirm.Click += (_, _) => Close(true);
            actions.Children.Add(confirm);
        }
        actions.Children.Add(dismiss);

        Content = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 20,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                actions
            }
        };
    }
}
