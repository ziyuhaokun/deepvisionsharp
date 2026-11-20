using System.Windows;

namespace YuUI;

/// <summary>调色板模式，对应源 Avalonia Palette.axaml 中 ThemeDictionaries 的 Light / Dark 两个分支。</summary>
public enum IvThemeMode
{
    Light,
    Dark,
}

/// <summary>
/// 运行时切换 yu-ui 调色板。WPF 没有 ThemeDictionaries 自动切换，因此把 Palette 字典整体换入
/// <c>Application.Resources</c> 的合并字典中，其它字典不受影响。
/// </summary>
/// <remarks>
/// Palette 常以嵌套形式被 YuUiTokens.xaml 合并，所以查找是深度优先的；命中后在该层原地替换，
/// 保持它在合并列表里的位置（合并字典按倒序解析，位置一旦改变就会改变优先级）。
/// </remarks>
public static class IvTheme
{
    private const string LightPalettePath = "Tokens/Palette.xaml";
    private const string DarkPalettePath = "Tokens/Palette.Dark.xaml";

    private static readonly Uri LightPaletteSource = new($"pack://application:,,,/yu-ui;component/{LightPalettePath}", UriKind.Absolute);
    private static readonly Uri DarkPaletteSource = new($"pack://application:,,,/yu-ui;component/{DarkPalettePath}", UriKind.Absolute);

    /// <summary>当前已应用的调色板模式。</summary>
    public static IvThemeMode Current { get; private set; } = IvThemeMode.Light;

    /// <summary>把指定 <paramref name="mode"/> 的调色板换入 <paramref name="app"/>。重复调用幂等。</summary>
    /// <exception cref="ArgumentNullException"><paramref name="app"/> 为 null。</exception>
    public static void Apply(Application app, IvThemeMode mode)
    {
        ArgumentNullException.ThrowIfNull(app);

        var source = mode == IvThemeMode.Dark ? DarkPaletteSource : LightPaletteSource;

        if (!TryReplacePalette(app.Resources, source))
        {
            app.Resources.MergedDictionaries.Insert(0, new ResourceDictionary { Source = source });
        }

        Current = mode;
    }

    private static bool TryReplacePalette(ResourceDictionary dictionary, Uri source)
    {
        var merged = dictionary.MergedDictionaries;
        for (var i = 0; i < merged.Count; i++)
        {
            var candidate = merged[i];
            if (IsPaletteSource(candidate.Source))
            {
                if (!HasSource(candidate.Source, source))
                {
                    merged[i] = new ResourceDictionary { Source = source };
                }

                return true;
            }

            if (TryReplacePalette(candidate, source))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPaletteSource(Uri? source)
    {
        var text = source?.OriginalString;
        return text is not null
            && (text.EndsWith(LightPalettePath, StringComparison.OrdinalIgnoreCase)
                || text.EndsWith(DarkPalettePath, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasSource(Uri? actual, Uri expected) =>
        actual is not null && string.Equals(actual.OriginalString, expected.OriginalString, StringComparison.OrdinalIgnoreCase);
}
