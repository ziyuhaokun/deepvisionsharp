using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Wpf.Ui.Controls;

namespace DeepVisionSharp.Shell.Views.Dialogs;

public enum StartupMessageKind
{
    Information,
    Error,

    /// <summary>Second-instance blocked; guide user back to the running app.</summary>
    AlreadyRunning
}

public partial class StartupMessageWindow : Window
{
    public StartupMessageWindow(string title, string message, StartupMessageKind kind)
    {
        InitializeComponent();

        Title = string.IsNullOrWhiteSpace(title) ? "DeepVisionSharp" : title;
        ApplyKind(kind, message);
    }

    private void ApplyKind(StartupMessageKind kind, string message)
    {
        var accentBrush = ResolveAccentBrush();

        switch (kind)
        {
            case StartupMessageKind.Error:
                TitleText.Text = "启动失败";
                MessageText.Text = string.IsNullOrWhiteSpace(message)
                    ? "应用启动时发生错误，详细信息已写入日志。"
                    : message;
                MessageIcon.Symbol = SymbolRegular.ErrorCircle24;
                IconContainer.Background = CreateTintBrush(Color.FromRgb(239, 68, 68), 0.16);
                MessageIcon.Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68));
                ConfirmButton.Content = "确定";
                break;

            case StartupMessageKind.AlreadyRunning:
                TitleText.Text = "应用已在运行";
                MessageText.Text = string.IsNullOrWhiteSpace(message)
                    ? "检测到已有运行中的实例。请切换到已打开的窗口继续使用，无需再次启动。"
                    : message;
                MessageIcon.Symbol = SymbolRegular.AppGeneric24;
                IconContainer.Background = CreateTintBrush(Color.FromRgb(245, 158, 11), 0.16);
                MessageIcon.Foreground = new SolidColorBrush(Color.FromRgb(245, 158, 11));
                ConfirmButton.Content = "知道了";
                break;

            default:
                TitleText.Text = Title;
                MessageText.Text = message;
                MessageIcon.Symbol = SymbolRegular.Info24;
                IconContainer.Background = CreateTintBrush(GetBrushColor(accentBrush), 0.16);
                MessageIcon.Foreground = accentBrush;
                ConfirmButton.Content = "确定";
                break;
        }
    }

    private Brush ResolveAccentBrush()
    {
        try
        {
            if (TryFindResource("SystemAccentColorPrimaryBrush") is Brush brush)
            {
                return brush;
            }
        }
        catch
        {
            // Theme may be partially loaded during early startup.
        }

        return new SolidColorBrush(Color.FromRgb(59, 130, 246));
    }

    private static Color GetBrushColor(Brush brush)
        => brush is SolidColorBrush solidColorBrush
            ? solidColorBrush.Color
            : Color.FromRgb(59, 130, 246);

    private static SolidColorBrush CreateTintBrush(Color color, double opacity)
        => new(Color.FromArgb((byte)(opacity * 255), color.R, color.G, color.B));

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Escape)
        {
            DialogResult = true;
            Close();
            e.Handled = true;
        }
    }

    private void OnWindowMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try
            {
                DragMove();
            }
            catch
            {
                // Ignore drag failures when capture is unavailable.
            }
        }
    }
}
