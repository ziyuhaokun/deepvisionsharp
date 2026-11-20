using Avalonia.Controls;

namespace DeepVisionSharp.Shell.Avalonia.Services;

/// <summary>
/// 当前 shell 主窗口。剪贴板、对话框、文本焦点探针都需要一个 <see cref="TopLevel"/>，
/// 端口实现由容器构造（拿不到窗口实例），所以窗口在显示时登记到这里、关闭时清空。
/// </summary>
public static class ShellWindowHost
{
    public static Window? Window { get; set; }
}
