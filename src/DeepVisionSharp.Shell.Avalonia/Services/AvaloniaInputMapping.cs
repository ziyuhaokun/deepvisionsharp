using Avalonia.Input;
using DeepVisionSharp.Presentation.Input;

namespace DeepVisionSharp.Shell.Avalonia.Services;

/// <summary>
/// 把 Avalonia 键盘输入映射到 ViewModel 用的 <see cref="UiKey"/> / <see cref="UiModifierKeys"/>。
/// 与 WPF 侧 WpfInputMapping 保持同一组键位；ViewModel 不处理的键映射为 <see cref="UiKey.None"/>。
/// </summary>
public static class AvaloniaInputMapping
{
    public static UiKey ToUiKey(Key key) => key switch
    {
        Key.Left => UiKey.Left,
        Key.Right => UiKey.Right,
        Key.Tab => UiKey.Tab,
        Key.S => UiKey.S,
        >= Key.D0 and <= Key.D9 => (UiKey)((int)UiKey.Digit0 + (key - Key.D0)),
        >= Key.NumPad0 and <= Key.NumPad9 => (UiKey)((int)UiKey.NumPad0 + (key - Key.NumPad0)),
        _ => UiKey.None
    };

    public static UiModifierKeys ToUiModifierKeys(KeyModifiers modifiers)
    {
        var result = UiModifierKeys.None;
        if (modifiers.HasFlag(KeyModifiers.Control))
        {
            result |= UiModifierKeys.Control;
        }

        if (modifiers.HasFlag(KeyModifiers.Shift))
        {
            result |= UiModifierKeys.Shift;
        }

        if (modifiers.HasFlag(KeyModifiers.Alt))
        {
            result |= UiModifierKeys.Alt;
        }

        if (modifiers.HasFlag(KeyModifiers.Meta))
        {
            result |= UiModifierKeys.Windows;
        }

        return result;
    }
}
