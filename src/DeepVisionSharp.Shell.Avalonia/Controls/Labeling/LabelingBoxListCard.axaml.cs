using Avalonia.Controls;
using Avalonia.Input;
using DeepVisionSharp.Presentation.ViewModels.Labeling;
using DeepVisionSharp.Shell.Avalonia.ViewModels;

namespace DeepVisionSharp.Shell.Avalonia.Controls.Labeling;

/// <summary>
/// 标注列表卡片：列表项就是 <see cref="BoxShapeViewModel"/>，点行等于把该框设为当前选中框。
/// 没有容器（设计时 / 无头渲染）时退回 stub 上下文，行为与真实 VM 同名同层。
/// </summary>
public partial class LabelingBoxListCard : UserControl
{
    public LabelingBoxListCard()
    {
        InitializeComponent();
    }

    private void OnAnnotationListItemPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left)
        {
            return;
        }

        if (sender is not Control { DataContext: BoxShapeViewModel box })
        {
            return;
        }

        switch (DataContext)
        {
            case LabelingViewModel viewModel:
                viewModel.Editor.SelectBoxCommand.Execute(box);
                break;
            case SampleLabelingContext stub:
                stub.SelectBox(box);
                break;
        }

        e.Handled = true;
    }
}