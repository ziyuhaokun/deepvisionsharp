using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Shell;

namespace YuUI.Controls;

/// <summary>
/// 自绘标题栏窗口：无系统标题栏，标题栏由模板画（左图标 + 标题，右侧 3 个 40×32 的窗口按钮）。
/// 源 ThemedWindow.cs 用 Avalonia 的 WindowDecorations.BorderOnly + ExtendClientAreaToDecorationsHint；
/// WPF 侧等价物是 WindowStyle=None + WindowChrome（保留缩放边框、最大化按工作区计算、贴边/拖拽还原）。
/// </summary>
public class ThemedWindow : Window
{
    public static readonly DependencyProperty CaptionHeightProperty =
        DependencyProperty.Register(
            nameof(CaptionHeight),
            typeof(double),
            typeof(ThemedWindow),
            new PropertyMetadata(52.0));

    private FrameworkElement? _titleBar;
    private ButtonBase? _minimizeButton;
    private ButtonBase? _maximizeButton;
    private ButtonBase? _closeButton;

    static ThemedWindow()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(ThemedWindow), new FrameworkPropertyMetadata(typeof(ThemedWindow)));
    }

    public ThemedWindow()
    {
        WindowStyle = System.Windows.WindowStyle.None;
        WindowChrome.SetWindowChrome(
            this,
            new WindowChrome
            {
                CaptionHeight = 0,
                ResizeBorderThickness = new Thickness(6),
                CornerRadius = new CornerRadius(0),
                GlassFrameThickness = new Thickness(0),
                UseAeroCaptionButtons = false,
            });
    }

    public double CaptionHeight
    {
        get => (double)GetValue(CaptionHeightProperty);
        set => SetValue(CaptionHeightProperty, value);
    }

    /// <summary>内容区自绘标题栏时，由标题栏区域调用以拖动窗口；最大化态不拖拽。</summary>
    public void BeginCaptionDrag(MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed || WindowState == WindowState.Maximized)
        {
            return;
        }

        DragMove();
    }

    public override void OnApplyTemplate()
    {
        DetachCaption();

        base.OnApplyTemplate();

        _titleBar = GetTemplateChild("PART_TitleBar") as FrameworkElement;
        _minimizeButton = GetTemplateChild("PART_MinimizeButton") as ButtonBase;
        _maximizeButton = GetTemplateChild("PART_MaximizeButton") as ButtonBase;
        _closeButton = GetTemplateChild("PART_CloseButton") as ButtonBase;

        if (_titleBar is not null)
        {
            _titleBar.MouseLeftButtonDown += OnTitleBarMouseLeftButtonDown;
        }

        if (_minimizeButton is not null)
        {
            _minimizeButton.Click += OnMinimizeClick;
        }

        if (_maximizeButton is not null)
        {
            _maximizeButton.Click += OnMaximizeClick;
        }

        if (_closeButton is not null)
        {
            _closeButton.Click += OnCloseClick;
        }
    }

    private void DetachCaption()
    {
        if (_titleBar is not null)
        {
            _titleBar.MouseLeftButtonDown -= OnTitleBarMouseLeftButtonDown;
            _titleBar = null;
        }

        if (_minimizeButton is not null)
        {
            _minimizeButton.Click -= OnMinimizeClick;
            _minimizeButton = null;
        }

        if (_maximizeButton is not null)
        {
            _maximizeButton.Click -= OnMaximizeClick;
            _maximizeButton = null;
        }

        if (_closeButton is not null)
        {
            _closeButton.Click -= OnCloseClick;
            _closeButton = null;
        }
    }

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void OnTitleBarMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            e.Handled = true;
            return;
        }

        BeginCaptionDrag(e);
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void OnMaximizeClick(object sender, RoutedEventArgs e)
    {
        ToggleMaximize();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
