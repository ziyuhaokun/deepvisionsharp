using Avalonia;
using Avalonia.Styling;
using DeepVisionSharp.Presentation.Ports;
using DeepVisionSharp.Presentation.Services;

namespace DeepVisionSharp.Shell.Avalonia.Services;

/// <summary>
/// Avalonia 主题适配：偏好映射到 <see cref="Application.RequestedThemeVariant"/>。
/// <see cref="ShellThemePreference.System"/> 按 Windows「应用使用浅色主题」注册表项判定，
/// 与 WPF Shell 的 <c>AppThemeService</c> 同一判定来源。
/// </summary>
public sealed class AvaloniaAppThemeService : IAppThemeService
{
    public ShellThemePreference Current { get; private set; } = ShellThemePreference.System;

    public event EventHandler? Changed;

    public void Apply(ShellThemePreference preference)
    {
        Current = preference;

        if (global::Avalonia.Application.Current is { } app)
        {
            app.RequestedThemeVariant = preference switch
            {
                ShellThemePreference.Dark => ThemeVariant.Dark,
                ShellThemePreference.Light => ThemeVariant.Light,
                _ => IsSystemDarkTheme() ? ThemeVariant.Dark : ThemeVariant.Light
            };
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static bool IsSystemDarkTheme()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch
        {
            return false;
        }
    }
}
