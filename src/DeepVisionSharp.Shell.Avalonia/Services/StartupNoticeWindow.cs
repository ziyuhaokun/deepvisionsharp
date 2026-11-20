using System.Diagnostics;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

using DeepVisionSharp.UI.Avalonia;
using DeepVisionSharp.UI.Avalonia.Controls;

namespace DeepVisionSharp.Shell.Avalonia.Services;

/// <summary>
/// 启动期/致命异常的用户可见提示窗：图标 + 标题 + 说明 + 一个关掉它的按钮（异常场景多一个「复制报错」）。
/// </summary>
/// <remarks>
/// <para>
/// 对应 WPF Shell 的 <c>StartupMessageWindow</c>：WPF 用 <c>MessageBox.ShowDialog</c> 阻塞到用户确认，
/// Avalonia 没有 MessageBox，这里给一个「关闭后由 <see cref="App"/> 收尾退出」的普通窗口，
/// 同样不吞掉任何失败信息，区别只是退出动作挂在窗口关闭事件上。
/// </para>
/// <para>
/// 用系统原生标题栏（<see cref="WindowDecorations.Full"/>）而不是 DeepVisionSharp.UI.Avalonia 的自绘标题栏：
/// 这条路径本身就是「外壳还没起来」，自绘标题栏没有可用的拖动/关闭逻辑，
/// 而原生标题栏自带关闭按钮，用户不会看到关不掉的窗口。
/// </para>
/// </remarks>
internal sealed class StartupNoticeWindow : Window
{
    private const double CardWidth = 440;
    private const int MaxMessageLength = 900;

    private StartupNoticeWindow(
        string heading,
        string message,
        Geometry iconData,
        Color iconColor,
        string? copyReportText,
        string confirmText)
    {
        Title = "DeepVisionSharp";
        Width = CardWidth;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = true;
        Topmost = true;
        Background = ResolveBrush("IvSurface1Brush") ?? Brushes.White;
        Content = BuildContent(heading, message, iconData, iconColor, copyReportText, confirmText);
        KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        };
    }

    /// <summary>启动失败（参数非法、容器建不起来、数据目录不可写等）。</summary>
    public static StartupNoticeWindow ForStartupFailure(string message) =>
        new(
            heading: "启动失败",
            message: Limit(message, "应用启动时发生错误，详细信息已写入日志。"),
            iconData: IvIcons.Error,
            iconColor: Color.Parse("#EF4444"),
            copyReportText: null,
            confirmText: "确定");

    /// <summary>第二个实例被挡下：引导用户回到已打开的窗口。</summary>
    public static StartupNoticeWindow ForAlreadyRunning() =>
        new(
            heading: "应用已在运行",
            message: "检测到已有运行中的实例。请切换到已打开的窗口继续使用，无需再次启动。",
            iconData: IvIcons.WindowMaximize,
            iconColor: Color.Parse("#F59E0B"),
            copyReportText: null,
            confirmText: "知道了");

    /// <summary>运行期未处理异常：日志已经写好，这里给人一个把完整报告复制走的机会。</summary>
    public static StartupNoticeWindow ForUnhandledException(string logDirectory, string report)
    {
        var message =
            "程序遇到未处理异常，但已尝试拦截以继续运行。\n\n详细信息已写入日志目录：\n" + logDirectory;
        return new StartupNoticeWindow(
            heading: "发生异常",
            message: message,
            iconData: IvIcons.Warning,
            iconColor: Color.Parse("#F59E0B"),
            copyReportText: report,
            confirmText: "关闭");
    }

    private Control BuildContent(
        string heading,
        string message,
        Geometry iconData,
        Color iconColor,
        string? copyReportText,
        string confirmText)
    {
        var icon = new Icon
        {
            Data = iconData,
            FontSize = 30,
            Foreground = new SolidColorBrush(iconColor),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var iconContainer = new Border
        {
            Width = 64,
            Height = 64,
            CornerRadius = new CornerRadius(32),
            Background = new SolidColorBrush(iconColor, 0.16),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = icon,
        };

        var headingText = new TextBlock
        {
            Text = heading,
            FontSize = 18,
            FontWeight = FontWeight.SemiBold,
            Foreground = ResolveBrush("IvText1Brush") ?? Brushes.Black,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 18, 0, 0),
        };

        var messageText = new TextBlock
        {
            Text = message,
            FontSize = 13,
            Foreground = ResolveBrush("IvText2Brush") ?? Brushes.DimGray,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 360,
            Margin = new Thickness(0, 10, 0, 0),
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10,
            Margin = new Thickness(0, 22, 0, 0),
        };

        if (!string.IsNullOrEmpty(copyReportText))
        {
            // 闭包不继承空值流分析，先取成非空局部量。
            var reportToCopy = copyReportText;
            var copyButton = new Button
            {
                Content = "复制报错",
                MinWidth = 96,
                Theme = ResolveControlTheme("IvButtonBaseStyle"),
            };
            copyButton.Click += async (_, _) => await TryCopyReportAsync(reportToCopy);
            buttons.Children.Add(copyButton);
        }

        var confirmButton = new Button
        {
            Content = confirmText,
            MinWidth = 96,
            IsDefault = true,
            Theme = ResolveControlTheme("IvPrimaryButtonStyle"),
        };
        confirmButton.Click += (_, _) => Close();
        buttons.Children.Add(confirmButton);

        var stack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(28, 26, 28, 24),
        };
        stack.Children.Add(iconContainer);
        stack.Children.Add(headingText);
        stack.Children.Add(messageText);
        stack.Children.Add(buttons);

        return stack;
    }

    private async Task TryCopyReportAsync(string report)
    {
        try
        {
            if (Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(report);
            }
        }
        catch (Exception ex)
        {
            // 剪贴板被别的进程占着是常事：只记录，不再弹第二个提示。
            Trace.WriteLine($"Copying crash report to clipboard failed: {ex.Message}");
        }
    }

    private static string Limit(string message, string fallback)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return fallback;
        }

        return message.Length <= MaxMessageLength ? message : message[..MaxMessageLength] + "…";
    }

    private static IBrush? ResolveBrush(string key) =>
        global::Avalonia.Application.Current?.TryFindResource(key, out var resource) == true
            ? resource as IBrush
            : null;

    private static ControlTheme? ResolveControlTheme(string key) =>
        global::Avalonia.Application.Current?.TryFindResource(key, out var resource) == true
            ? resource as ControlTheme
            : null;
}
