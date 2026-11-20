using Avalonia.Controls;
using DeepVisionSharp.Shell.Avalonia.ViewModels;

namespace DeepVisionSharp.Shell.Avalonia.Views;

/// <summary>
/// 设置页：外观与通知 / 存储 / 关于 / 高级（F12 解锁）四个分区，另加 Python 环境分区。
/// 本波用 stub 数据，不接业务 ViewModel，也不落盘。
/// </summary>
public partial class SettingsPage : UserControl
{
    public SettingsPage()
    {
        InitializeComponent();
        DataContext = new SampleSettingsContext();
    }
}
