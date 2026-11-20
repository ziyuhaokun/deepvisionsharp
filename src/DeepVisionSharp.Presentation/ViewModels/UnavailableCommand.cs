using System.Windows.Input;

namespace DeepVisionSharp.Presentation.ViewModels;

/// <summary>
/// 本仓库没有对应后端的功能入口：deepvision 的页面保留了按钮/菜单项与绑定路径，
/// 这里给它一个「始终不可执行」的命令，让入口以禁用态出现，而不是假装可用或静默消失。
/// 每个使用点都在属性的 XML 注释里写明缺什么后端。
/// </summary>
public sealed class UnavailableCommand : ICommand
{
    public static UnavailableCommand Instance { get; } = new();

    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }

    public bool CanExecute(object? parameter) => false;

    public void Execute(object? parameter)
    {
    }
}
