using DeepVisionSharp.Presentation.Ports;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace DeepVisionSharp.Shell.Services;

/// <summary>
/// WPF focus-tree probe for the text-input port: walks up from the focused element to decide whether
/// the user is typing (text box, password box, editable or open combo box).
/// </summary>
public sealed class WpfTextInputFocusProbe : ITextInputFocusProbe
{
    public bool IsTextInputFocused()
    {
        var focused = Keyboard.FocusedElement as DependencyObject;
        while (focused is not null)
        {
            if (focused is TextBoxBase or PasswordBox)
            {
                return true;
            }

            if (focused is ComboBox comboBox && (comboBox.IsEditable || comboBox.IsDropDownOpen))
            {
                return true;
            }

            focused = LogicalTreeHelper.GetParent(focused) as DependencyObject
                ?? VisualTreeHelper.GetParent(focused);
        }

        return false;
    }
}
