using System.Windows;

namespace DeepVisionSharp.Shell.Views;

/// <summary>
/// DeepVisionSharp.UI 验收探针窗：一屏展示令牌、排版、按钮、输入、容器、图标与表单，供离屏渲染截图核对移植结果。
/// 无参构造 + InitializeComponent，不接 DI、不含业务逻辑（由测试/工具直接 new 出来截图）。
/// </summary>
public partial class DeepVisionSharpUIStyleProbeWindow : Window
{
    public DeepVisionSharpUIStyleProbeWindow()
    {
        InitializeComponent();
    }
}
