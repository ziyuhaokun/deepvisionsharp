using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Key = System.Windows.Input.Key;

namespace DeepVisionSharp.Shell.Services.Labeling;

internal static class LabelingKeyboardNavigation
{
    public static bool IsTextInputFocused()
    {
        var focused = Keyboard.FocusedElement as DependencyObject;
        while (focused is not null)
        {
            if (focused is System.Windows.Controls.Primitives.TextBoxBase or PasswordBox)
            {
                return true;
            }

            if (focused is System.Windows.Controls.ComboBox comboBox && (comboBox.IsEditable || comboBox.IsDropDownOpen))
            {
                return true;
            }

            var focusedType = focused.GetType();
            if (focusedType.Name == "TextBox"
                && focusedType.Namespace is not null
                && focusedType.Namespace.StartsWith("Wpf.Ui", StringComparison.Ordinal))
            {
                return true;
            }

            focused = LogicalTreeHelper.GetParent(focused) as DependencyObject
                ?? VisualTreeHelper.GetParent(focused);
        }

        return false;
    }

    public static int GetLabelHotkeyIndex(Key key)
    {
        return key switch
        {
            >= Key.D1 and <= Key.D9 => key - Key.D1,
            Key.D0 => 9,
            >= Key.NumPad1 and <= Key.NumPad9 => key - Key.NumPad1,
            Key.NumPad0 => 9,
            _ => -1
        };
    }
}
