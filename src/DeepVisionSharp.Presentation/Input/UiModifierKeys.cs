namespace DeepVisionSharp.Presentation.Input;

/// <summary>
/// Framework-neutral modifier state that accompanies <see cref="UiKey"/>. Shells translate their own
/// modifier type (WPF <c>ModifierKeys</c>, Avalonia <c>KeyModifiers</c>) onto these flags.
/// </summary>
[Flags]
public enum UiModifierKeys
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Windows = 8
}
