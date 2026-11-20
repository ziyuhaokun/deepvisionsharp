namespace DeepVisionSharp.Shell.Avalonia.Services;

/// <summary>
/// 启动参数的轻量扫描。<see cref="DeepVisionSharp.Presentation.Services.DeepVisionLaunchOptions.Parse"/>
/// 失败时也要能判断「这条进程是不是无人值守」，所以这里只认标志本身，不参与参数校验。
/// </summary>
internal static class StartupArguments
{
    /// <summary>含 <c>--ui-test</c>：FlaUI/CI 等无人值守场景，禁止任何需要人点掉的提示。</summary>
    public static bool ContainsUiTest(IReadOnlyList<string>? args)
    {
        if (args is null)
        {
            return false;
        }

        for (var i = 0; i < args.Count; i++)
        {
            if (string.Equals(args[i], "--ui-test", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
