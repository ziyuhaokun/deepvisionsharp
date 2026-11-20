using Avalonia.Controls;
using DeepVisionSharp.Presentation.Services.Navigation;

namespace DeepVisionSharp.Shell.Avalonia.Services.Hosting;

/// <summary>
/// 内容区宿主：主窗口实现，导航服务只决定「显示哪一页」。
/// <paramref name="page"/> 为 null 表示该路由还没有 Avalonia 页面，宿主自行显示占位页。
/// </summary>
public interface IPageHost
{
    bool IsAttached { get; }

    void Show(Control? page, AppRoute route);
}
