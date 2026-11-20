using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using DeepVisionSharp.Presentation.Ports;

namespace DeepVisionSharp.Shell.Avalonia.Services;

/// <summary>
/// Avalonia 焦点树探针：从当前焦点元素沿视觉树向上判断用户是否正在输入
/// （文本框、数字框、可编辑或已展开的下拉框）。快捷键处理据此让路。
/// </summary>
public sealed class AvaloniaTextInputFocusProbe : ITextInputFocusProbe
{
    public bool IsTextInputFocused()
    {
        var topLevel = TopLevel.GetTopLevel(ShellWindowHost.Window);
        var focused = topLevel?.FocusManager?.GetFocusedElement();
        if (focused is not Visual visual)
        {
            return false;
        }

        foreach (var element in visual.GetSelfAndVisualAncestors())
        {
            if (element is TextBox)
            {
                return true;
            }

            if (element is ComboBox { IsEditable: true } or ComboBox { IsDropDownOpen: true })
            {
                return true;
            }
        }

        return false;
    }
}
