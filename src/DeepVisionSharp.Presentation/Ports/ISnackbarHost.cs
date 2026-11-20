namespace DeepVisionSharp.Presentation.Ports;

/// <summary>提示条语义，与 deepvision 的 snackbar 四种语义一一对应。</summary>
public enum SnackbarKind
{
    Info,
    Success,
    Warning,
    Error
}

/// <summary>
/// 提示条宿主（由 shell 主窗口实现并挂到 <see cref="Services.ShellSnackbarNotifier"/>）。
/// 服务只负责语义与日志，宿主只负责画。
/// </summary>
public interface ISnackbarHost
{
    /// <summary>宿主是否已就绪；未就绪时服务丢弃提示（与 WPF-UI 旧行为一致：启动早期不崩）。</summary>
    bool IsAttached { get; }

    void Show(string title, string message, SnackbarKind kind, TimeSpan duration);
}
