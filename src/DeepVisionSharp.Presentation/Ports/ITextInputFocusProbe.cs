namespace DeepVisionSharp.Presentation.Ports;

/// <summary>
/// Asks the host shell whether keyboard focus currently sits in a text input. Shortcut handling
/// must stay out of the way while the user is typing, and only the UI framework knows the visual
/// tree, so the probe lives with the shell.
/// </summary>
public interface ITextInputFocusProbe
{
    bool IsTextInputFocused();
}
