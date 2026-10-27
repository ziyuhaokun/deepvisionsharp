namespace DeepVisionSharp.Shell.Services;

/// <summary>
/// Parsed process launch options. Used for UI-test harness isolation.
/// </summary>
public sealed class DeepVisionLaunchOptions
{
    public static DeepVisionLaunchOptions Default { get; } = new();

    public bool IsUiTest { get; init; }

    public string? DataRoot { get; init; }

    /// <summary>Explicit full-profile isolation; --data-root alone retains its existing meaning.</summary>
    public string? VerificationRoot { get; init; }

    public bool SkipSingleInstance { get; init; }

    /// <summary>
    /// Parses process args. Throws when <c>--ui-test</c> lacks a usable <c>--data-root</c>,
    /// or when <c>--data-root</c> is present without a path value.
    /// </summary>
    public static DeepVisionLaunchOptions Parse(IReadOnlyList<string> args)
    {
        var isUiTest = false;
        string? dataRoot = null;
        var skipSingleInstance = false;
        var sawDataRootFlag = false;
        string? verificationRoot = null;
        var sawVerificationRoot = false;

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (string.Equals(arg, "--verification-root", StringComparison.OrdinalIgnoreCase))
            {
                sawVerificationRoot = true;
                verificationRoot = i + 1 < args.Count && !args[i + 1].StartsWith('-') ? args[++i] : null;
                continue;
            }

            if (arg.StartsWith("--verification-root=", StringComparison.OrdinalIgnoreCase))
            {
                sawVerificationRoot = true;
                verificationRoot = arg["--verification-root=".Length..];
                continue;
            }

            if (string.Equals(arg, "--ui-test", StringComparison.OrdinalIgnoreCase))
            {
                isUiTest = true;
                continue;
            }

            if (string.Equals(arg, "--skip-single-instance", StringComparison.OrdinalIgnoreCase))
            {
                skipSingleInstance = true;
                continue;
            }

            if (string.Equals(arg, "--data-root", StringComparison.OrdinalIgnoreCase))
            {
                sawDataRootFlag = true;
                if (i + 1 < args.Count && !args[i + 1].StartsWith('-'))
                {
                    dataRoot = args[++i];
                }

                continue;
            }

            if (arg.StartsWith("--data-root=", StringComparison.OrdinalIgnoreCase))
            {
                sawDataRootFlag = true;
                dataRoot = arg["--data-root=".Length..];
            }
        }

        if (sawDataRootFlag && string.IsNullOrWhiteSpace(dataRoot))
        {
            throw new ArgumentException(
                "--data-root requires a non-empty path value (e.g. --data-root C:\\temp\\dv-ui).");
        }

        if (isUiTest && string.IsNullOrWhiteSpace(dataRoot))
        {
            throw new ArgumentException(
                "--ui-test requires --data-root <path> so SQLite/project files never touch " +
                "%LOCALAPPDATA%\\DeepVisionSharp\\Data.");
        }

        if (isUiTest)
        {
            skipSingleInstance = true;
        }

        if (sawVerificationRoot)
        {
            if (!isUiTest || string.IsNullOrWhiteSpace(verificationRoot))
            {
                throw new ArgumentException("--verification-root <path> requires --ui-test and a non-empty path.");
            }

            verificationRoot = System.IO.Path.GetFullPath(verificationRoot.Trim());
            var relativeData = System.IO.Path.GetRelativePath(verificationRoot, System.IO.Path.GetFullPath(dataRoot!.Trim()));
            if (relativeData == "." || relativeData == ".." || relativeData.StartsWith(".." + System.IO.Path.DirectorySeparatorChar)
                || System.IO.Path.IsPathRooted(relativeData))
            {
                throw new ArgumentException("--data-root must be a child of --verification-root (for example <run>/Data).");
            }
        }

        return new DeepVisionLaunchOptions
        {
            IsUiTest = isUiTest,
            DataRoot = string.IsNullOrWhiteSpace(dataRoot) ? null : dataRoot.Trim(),
            VerificationRoot = verificationRoot,
            SkipSingleInstance = skipSingleInstance
        };
    }
}
