using Avalonia.Controls;

namespace DeepVisionSharp.Shell.Avalonia.Controls.Training;

/// <summary>
/// 训练设置面板：与 TrainingRunPanel 共用同一份 DataContext（stub 的 Config / JobList）。
/// 本波只做结构与绑定，表单不写回业务层。
/// </summary>
public partial class TrainingSettingsPanel : UserControl
{
    public TrainingSettingsPanel()
    {
        InitializeComponent();
    }
}
