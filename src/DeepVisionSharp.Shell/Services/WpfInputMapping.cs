using DeepVisionSharp.Presentation.Input;

using WpfKey = System.Windows.Input.Key;
using WpfModifierKeys = System.Windows.Input.ModifierKeys;

namespace DeepVisionSharp.Shell.Services;

/// <summary>
/// Maps WPF keyboard input onto the framework-neutral <see cref="UiKey"/> / <see cref="UiModifierKeys"/>
/// that ViewModel shortcut handlers take. Keys the ViewModels do not handle map to <see cref="UiKey.None"/>.
/// </summary>
public static class WpfInputMapping
{
    public static UiKey ToUiKey(WpfKey key) => key switch
    {
        WpfKey.Left => UiKey.Left,
        WpfKey.Right => UiKey.Right,
        WpfKey.Tab => UiKey.Tab,
        WpfKey.S => UiKey.S,
        >= WpfKey.D0 and <= WpfKey.D9 => (UiKey)((int)UiKey.Digit0 + (key - WpfKey.D0)),
        >= WpfKey.NumPad0 and <= WpfKey.NumPad9 => (UiKey)((int)UiKey.NumPad0 + (key - WpfKey.NumPad0)),
        _ => UiKey.None
    };

    public static UiModifierKeys ToUiModifierKeys(WpfModifierKeys modifiers)
    {
        var result = UiModifierKeys.None;
        if (modifiers.HasFlag(WpfModifierKeys.Control))
        {
            result |= UiModifierKeys.Control;
        }

        if (modifiers.HasFlag(WpfModifierKeys.Shift))
        {
            result |= UiModifierKeys.Shift;
        }

        if (modifiers.HasFlag(WpfModifierKeys.Alt))
        {
            result |= UiModifierKeys.Alt;
        }

        if (modifiers.HasFlag(WpfModifierKeys.Windows))
        {
            result |= UiModifierKeys.Windows;
        }

        return result;
    }
}
