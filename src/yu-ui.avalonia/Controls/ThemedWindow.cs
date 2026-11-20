using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;

namespace YuUI.Controls;

/// <summary>
/// 自绘标题栏窗口：内容区延伸进非客户区，右上角固定三个 40×32 的窗口按钮。
/// 对应 WPF 版 YuUI.Controls.ThemedWindow 的 WindowChrome 行为。
/// </summary>
public class ThemedWindow : Window
{
    public static readonly StyledProperty<double> CaptionHeightProperty =
        AvaloniaProperty.Register<ThemedWindow, double>(nameof(CaptionHeight), 52.0);

    static ThemedWindow()
    {
        ExtendClientAreaToDecorationsHintProperty.OverrideDefaultValue<ThemedWindow>(true);
        ExtendClientAreaTitleBarHeightHintProperty.OverrideDefaultValue<ThemedWindow>(-1);
    }

    public ThemedWindow()
    {
        WindowDecorations = WindowDecorations.BorderOnly;
    }

    protected override Type StyleKeyOverride => typeof(ThemedWindow);

    public double CaptionHeight
    {
        get => GetValue(CaptionHeightProperty);
        set => SetValue(CaptionHeightProperty, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        if (e.NameScope.Find<Button>("PART_MinimizeButton") is { } minimize)
        {
            minimize.Click += (_, _) => WindowState = WindowState.Minimized;
        }

        if (e.NameScope.Find<Button>("PART_MaximizeButton") is { } maximize)
        {
            maximize.Click += (_, _) => ToggleMaximize();
        }

        if (e.NameScope.Find<Button>("PART_CloseButton") is { } close)
        {
            close.Click += (_, _) => Close();
        }
    }

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    /// <summary>内容区自绘标题栏时，由标题栏区域调用以拖动窗口。</summary>
    public void BeginCaptionDrag(PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }
}
