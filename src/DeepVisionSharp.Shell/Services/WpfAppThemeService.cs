using Wpf.Ui.Appearance;

namespace DeepVisionSharp.Shell.Services;

public sealed class WpfAppThemeService : IAppThemeService
{
    public void Apply(ShellThemePreference preference)
    {
        var theme = preference switch
        {
            ShellThemePreference.Dark => ApplicationTheme.Dark,
            ShellThemePreference.Light => ApplicationTheme.Light,
            _ => IsSystemDarkTheme() ? ApplicationTheme.Dark : ApplicationTheme.Light
        };

        ApplicationThemeManager.Apply(theme);
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

