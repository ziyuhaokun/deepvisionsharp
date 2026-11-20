namespace DeepVisionSharp.Presentation.Input;

/// <summary>
/// Framework-neutral keyboard key for ViewModel shortcut handling. Shells map their own key type
/// (WPF <c>Key</c>, Avalonia <c>Key</c>) onto these values and map anything they cannot represent to
/// <see cref="None"/>; only keys listed here are handled by ViewModels.
/// </summary>
public enum UiKey
{
    None,
    Left,
    Right,
    Tab,
    S,

    // 数字键与类别热键一一对应，保持连续：中间不要插入其它值。
    Digit0,
    Digit1,
    Digit2,
    Digit3,
    Digit4,
    Digit5,
    Digit6,
    Digit7,
    Digit8,
    Digit9,

    NumPad0,
    NumPad1,
    NumPad2,
    NumPad3,
    NumPad4,
    NumPad5,
    NumPad6,
    NumPad7,
    NumPad8,
    NumPad9
}
