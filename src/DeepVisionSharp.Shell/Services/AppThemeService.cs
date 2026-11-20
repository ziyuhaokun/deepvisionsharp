using YuUI;
using DeepVisionSharp.Presentation.Ports;
using DeepVisionSharp.Presentation.Services;

namespace DeepVisionSharp.Shell.Services;

public sealed class AppThemeService : IAppThemeService
{
    public ShellThemePreference Current { get; private set; } = ShellThemePreference.System;

    public event EventHandler? Changed;

    public void Apply(ShellThemePreference preference)
    {
        Current = preference;

        var mode = preference switch
        {
            ShellThemePreference.Dark => IvThemeMode.Dark,
            ShellThemePreference.Light => IvThemeMode.Light,
            _ => IsSystemDarkTheme() ? IvThemeMode.Dark : IvThemeMode.Light
        };

        if (System.Windows.Application.Current is { } app)
        {
            IvTheme.Apply(app, mode);
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
