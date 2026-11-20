using Avalonia.Controls;
using Avalonia.Input;

namespace DeepVisionSharp.Shell.Avalonia.Controls.Labeling;

/// <summary>
/// 标注列表卡片。deepvision 版在这些输入点抓撤销快照、并按 Id 选中标注；
/// 本波没有编辑历史与选择协调器，事件保留为空实现，只维持 XAML 的接线与列表静态渲染。
/// </summary>
public partial class LabelingBoxListCard : UserControl
{
    public LabelingBoxListCard()
    {
        InitializeComponent();
    }

    private void OnTranscriptionGotFocus(object? sender, FocusChangedEventArgs e)
    {
    }

    private void OnIgnorePointerPressed(object? sender, PointerPressedEventArgs e)
    {
    }

    private void OnIgnoreKeyDown(object? sender, KeyEventArgs e)
    {
    }

    private void OnAnnotationListItemPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
    }
}
