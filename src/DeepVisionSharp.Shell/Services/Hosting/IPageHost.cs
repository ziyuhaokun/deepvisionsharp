namespace DeepVisionSharp.Shell.Services.Hosting;

/// <summary>
/// 页面宿主（由 <c>MainWindow</c> 的 <c>PageHost</c> ContentControl 实现）。
/// 取代 WPF-UI 的 <c>INavigationService</c>：导航服务负责路由映射与工作区校验，宿主只负责把实例放到内容区。
/// </summary>
public interface IPageHost
{
    bool IsAttached { get; }

    object? CurrentPage { get; }

    /// <summary>把页面实例放进内容区（同一个实例可重复传入）。</summary>
    void Show(object page);
}
