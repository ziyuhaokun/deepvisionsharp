using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace YuUI.Controls;

/// <summary>对话框关闭方式。</summary>
public enum DialogChoice
{
    None,
    Primary,
    Secondary,
}

/// <summary>
/// 对话框容器：遮罩 + 居中卡片（最小 420 宽、24,20,24,18 内边距、标题行带 22 号图标）。
/// 行为与源 InCube.UI.Avalonia/Controls/Dialog.cs 一致：同一时刻只承载一个对话框，新的 ShowAsync 会以 None 结束上一个；
/// Hide 前触发可取消的 Closing；Esc 关闭；关闭后把焦点还给打开前的元素。
/// WPF 侧用 Visibility 承担源里的 IsVisible 开关。
/// </summary>
public class Dialog : ContentControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(Dialog), new PropertyMetadata(defaultValue: null));

    public static readonly DependencyProperty FooterProperty =
        DependencyProperty.Register(nameof(Footer), typeof(object), typeof(Dialog), new PropertyMetadata(defaultValue: null));

    private static readonly DependencyPropertyKey IsOpenPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(IsOpen), typeof(bool), typeof(Dialog), new PropertyMetadata(false));

    public static readonly DependencyProperty IsOpenProperty = IsOpenPropertyKey.DependencyProperty;

    private TaskCompletionSource<DialogChoice>? _pending;
    private IInputElement? _previousFocus;

    static Dialog()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(Dialog), new FrameworkPropertyMetadata(typeof(Dialog)));
    }

    public string? Title
    {
        get => (string?)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>底部按钮区（右对齐）。</summary>
    public object? Footer
    {
        get => GetValue(FooterProperty);
        set => SetValue(FooterProperty, value);
    }

    public bool IsOpen => (bool)GetValue(IsOpenProperty);

    /// <summary>关闭前触发；处理方把 Cancel 设为 true 即可阻止关闭。</summary>
    public event EventHandler<CancelEventArgs>? Closing;

    public Task<DialogChoice> ShowAsync()
    {
        _pending?.TrySetResult(DialogChoice.None);
        _pending = new TaskCompletionSource<DialogChoice>();

        if (!IsOpen)
        {
            _previousFocus = Keyboard.FocusedElement;
        }

        SetValue(IsOpenPropertyKey, true);
        SetValue(VisibilityProperty, Visibility.Visible);
        return _pending.Task;
    }

    /// <summary>尝试关闭；被 Closing 取消时返回 false。</summary>
    public bool Hide(DialogChoice result = DialogChoice.None)
    {
        if (!IsOpen)
        {
            return false;
        }

        var closing = new CancelEventArgs();
        Closing?.Invoke(this, closing);
        if (closing.Cancel)
        {
            return false;
        }

        SetValue(IsOpenPropertyKey, false);
        SetValue(VisibilityProperty, Visibility.Collapsed);

        var previousFocus = _previousFocus;
        _previousFocus = null;
        if (previousFocus is not null)
        {
            Keyboard.Focus(previousFocus);
        }

        var pending = _pending;
        _pending = null;
        pending?.TrySetResult(result);
        return true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (IsOpen && !e.Handled && e.Key == Key.Escape)
        {
            Hide(DialogChoice.None);
            e.Handled = true;
        }
    }
}
