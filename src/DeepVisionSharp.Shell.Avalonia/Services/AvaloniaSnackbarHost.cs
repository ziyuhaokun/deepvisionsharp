using DeepVisionSharp.Presentation.Ports;

namespace DeepVisionSharp.Shell.Avalonia.Services;

/// <summary>一条待显示的提示：语义 + 文案 + 停留时长。</summary>
public sealed record SnackbarRequest(string Title, string Message, SnackbarKind Kind, TimeSpan Duration);

/// <summary>
/// 提示条宿主适配：服务只交代语义与时长，视觉由主窗口订阅 <see cref="ShowRequested"/> 后自己画。
/// 主窗口还没挂上时 <see cref="IsAttached"/> 为 false，<c>ShellSnackbarNotifier</c> 会丢弃早期提示（与 WPF 版一致）。
/// </summary>
public sealed class AvaloniaSnackbarHost : ISnackbarHost
{
    public event Action<SnackbarRequest>? ShowRequested;

    public bool IsAttached => ShowRequested is not null;

    public void Show(string title, string message, SnackbarKind kind, TimeSpan duration)
        => ShowRequested?.Invoke(new SnackbarRequest(title, message, kind, duration));
}
