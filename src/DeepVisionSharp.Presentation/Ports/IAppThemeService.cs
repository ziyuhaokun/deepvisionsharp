using DeepVisionSharp.Presentation.Services;

namespace DeepVisionSharp.Presentation.Ports;

/// <summary>
/// Theme port. The shell owns palette mechanics; ViewModels only read and request the preference.
/// </summary>
public interface IAppThemeService
{
    /// <summary>当前生效的偏好。</summary>
    ShellThemePreference Current { get; }

    /// <summary>偏好或系统主题变化后触发（宿主据此刷新标题栏图标）。</summary>
    event EventHandler? Changed;

    void Apply(ShellThemePreference preference);
}
