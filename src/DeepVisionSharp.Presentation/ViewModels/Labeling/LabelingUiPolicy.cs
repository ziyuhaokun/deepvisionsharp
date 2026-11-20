using CommunityToolkit.Mvvm.ComponentModel;

namespace DeepVisionSharp.Presentation.ViewModels.Labeling;

/// <summary>
/// 标注页的界面策略：把「这个工作流有没有这项能力」和「当前样本能不能用」收敛成一组可绑定布尔，
/// 免得 XAML 里对多个来源拼 MultiBinding。分类只按整图打类，框工具与标注模式行只在检测工作流出现。
/// </summary>
public sealed class LabelingUiPolicy : ObservableObject
{
    private readonly LabelingWorkspaceContext _workspace;
    private bool _hasBoxes;

    internal LabelingUiPolicy(LabelingWorkspaceContext workspace)
    {
        _workspace = workspace;
    }

    /// <summary>矩形工具：检测工作流派框用，分类项目没有框。</summary>
    public bool CanUseRectangleTool => _workspace.IsDetectionProject;

    /// <summary>「标注模式」整行：同上，只在检测工作流显示。</summary>
    public bool ShowsAnnotationModeTools => _workspace.IsDetectionProject;

    /// <summary>画布复位恒可用：任何缩放下都能回「适应窗口」，与样本/框的状态无关。</summary>
    public bool CanUseAutoFit => true;

    /// <summary>清空按钮的可执行条件与「当前样本有没有框」同源。</summary>
    public bool CanClearAllAnnotations => _hasBoxes;

    /// <summary>由编辑器把当前的框状态推过来；编辑器是唯一知道这个状态的地方。</summary>
    internal void TrackBoxes(bool hasBoxes)
    {
        if (_hasBoxes == hasBoxes)
        {
            return;
        }

        _hasBoxes = hasBoxes;
        OnPropertyChanged(nameof(CanClearAllAnnotations));
    }

    internal void NotifyWorkflowChanged()
    {
        OnPropertyChanged(nameof(CanUseRectangleTool));
        OnPropertyChanged(nameof(ShowsAnnotationModeTools));
    }
}
