using DeepVisionSharp.Presentation.Input;

namespace DeepVisionSharp.Presentation.Services.Labeling;

/// <summary>标注页数字键解析：主键盘 / 小键盘数字映射到类别索引（0 基），其它键返回 -1。</summary>
internal static class LabelingKeyboardNavigation
{
    public static int GetLabelHotkeyIndex(UiKey key)
    {
        return key switch
        {
            >= UiKey.Digit1 and <= UiKey.Digit9 => (int)key - (int)UiKey.Digit1,
            UiKey.Digit0 => 9,
            >= UiKey.NumPad1 and <= UiKey.NumPad9 => (int)key - (int)UiKey.NumPad1,
            UiKey.NumPad0 => 9,
            _ => -1
        };
    }
}
