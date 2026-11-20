using Avalonia.Controls;
using DeepVisionSharp.Shell.Avalonia.ViewModels;

namespace DeepVisionSharp.Shell.Avalonia.Views;

/// <summary>模型页。本波用 stub 数据，不接业务 ViewModel，也没有 OpenVINO 转换流程。</summary>
public partial class ModelsPage : UserControl
{
    public ModelsPage()
    {
        InitializeComponent();
        DataContext = new SampleModelsContext();
    }
}
