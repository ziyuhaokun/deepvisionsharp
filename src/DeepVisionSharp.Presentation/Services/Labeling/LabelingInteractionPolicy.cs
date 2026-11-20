using DeepVisionSharp.Presentation.Input;

namespace DeepVisionSharp.Presentation.Services.Labeling;

/// <summary>差异分类工作流下，未修饰数字键 / Tab 的业务语义。</summary>
internal enum LabelingDigitShortcut
{
    None,
    ImageClassOk,
    ImageClassNg,
    NgAndMove,
    NavigateNext
}

/// <summary>
/// 标注页快捷键语义策略：把「按了哪个未修饰键」解析成业务动作（差异分类专用）。
/// 差异分类占用 1/2/3/Tab；其它数字留给类别热键（<see cref="LabelingKeyboardNavigation.GetLabelHotkeyIndex"/>）。
/// </summary>
internal static class LabelingInteractionPolicy
{
    public static LabelingDigitShortcut ResolveUnmodifiedDigit(UiKey key) => key switch
    {
        UiKey.Digit1 or UiKey.NumPad1 => LabelingDigitShortcut.ImageClassOk,
        UiKey.Digit2 or UiKey.NumPad2 => LabelingDigitShortcut.ImageClassNg,
        UiKey.Digit3 or UiKey.NumPad3 => LabelingDigitShortcut.NgAndMove,
        UiKey.Tab => LabelingDigitShortcut.NavigateNext,
        _ => LabelingDigitShortcut.None
    };
}
