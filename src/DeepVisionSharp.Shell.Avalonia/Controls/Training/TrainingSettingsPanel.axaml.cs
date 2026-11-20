using Avalonia.Controls;

namespace DeepVisionSharp.Shell.Avalonia.Controls.Training;

/// <summary>
/// 训练设置面板：与 TrainingRunPanel 共用同一份 DataContext（stub 的 Config / JobList）。
/// 表单靠 TwoWay 绑定写入 Config，Presentation 的 TrainingDraftFormSaver 再负责把草稿写回业务层。
/// </summary>
public partial class TrainingSettingsPanel : UserControl
{
    public TrainingSettingsPanel()
    {
        InitializeComponent();
    }
}
