using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace DeepVisionSharp.UI.Avalonia.Controls;

/// <summary>对话框关闭方式。</summary>
public enum DialogChoice
{
    None,
    Primary,
    Secondary,
}

/// <summary>
/// 对话框容器：遮罩 + 居中卡片（最小 420 宽、24,20,24,18 内边距、标题行带 22 号图标）。
/// 对应 WPF 版 DeepVisionSharp.UI.Avalonia.Controls.Dialog：同一时刻只承载一个对话框，新的 ShowAsync 会以 None 结束上一个；
/// Hide 前触发可取消的 Closing；Esc 关闭；关闭后把焦点还给打开前的元素。
/// </summary>
public class Dialog : ContentControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<Dialog, string?>(nameof(Title));

    public static readonly StyledProperty<object?> FooterProperty =
        AvaloniaProperty.Register<Dialog, object?>(nameof(Footer));

    public static readonly DirectProperty<Dialog, bool> IsOpenProperty =
        AvaloniaProperty.RegisterDirect<Dialog, bool>(nameof(IsOpen), o => o.IsOpen);

    private TaskCompletionSource<DialogChoice>? _pending;
    private IInputElement? _previousFocus;
    private bool _isOpen;

    protected override Type StyleKeyOverride => typeof(Dialog);

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>底部按钮区（右对齐）。</summary>
    public object? Footer
    {
        get => GetValue(FooterProperty);
        set => SetValue(FooterProperty, value);
    }

    public bool IsOpen
    {
        get => _isOpen;
        private set => SetAndRaise(IsOpenProperty, ref _isOpen, value);
    }

    /// <summary>关闭前触发；处理方把 Cancel 设为 true 即可阻止关闭。</summary>
    public event EventHandler<CancelEventArgs>? Closing;

    public Task<DialogChoice> ShowAsync()
    {
        _pending?.TrySetResult(DialogChoice.None);
        _pending = new TaskCompletionSource<DialogChoice>();

        if (!IsOpen)
        {
            _previousFocus = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        }

        IsOpen = true;
        IsVisible = true;
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

        IsOpen = false;
        IsVisible = false;

        var previousFocus = _previousFocus;
        _previousFocus = null;
        previousFocus?.Focus();

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
