using System.IO;

namespace DeepVisionSharp.Shell.Services;

/// <summary>
/// One Shell profile per process, including last-chance/static UI paths before DI is available.
/// Creating a host selects that process profile; concurrent profiles use separate processes.
/// </summary>
public sealed class ShellRuntimePaths
{
    public static ShellRuntimePaths Current { get; private set; } = FromOptions(DeepVisionLaunchOptions.Default);

    public bool IsVerification { get; private init; }
    public string DataDirectory { get; private init; } = "";
    public string SettingsDirectory { get; private init; } = "";
    public string? LegacySettingsPath { get; private init; }
    public string LogDirectory { get; private init; } = "";
    public string ModelsDirectory { get; private init; } = "";
    public string CrashDumpDirectory { get; private init; } = "";
    public string DiagnosticDirectory { get; private init; } = "";
    public string ExceptionJournalPath => Path.Combine(DiagnosticDirectory, "unhandled-exceptions.jsonl");

    public static ShellRuntimePaths FromOptions(DeepVisionLaunchOptions options)
    {
        if (options.VerificationRoot is { } root)
        {
            // Revalidate even when options were constructed rather than parsed.
            var validated = DeepVisionLaunchOptions.Parse([
                "--ui-test", "--data-root", options.DataRoot ?? "", "--verification-root", root]);
            if (!options.IsUiTest)
                throw new ArgumentException("A verification profile requires UI-test mode.", nameof(options));
            root = validated.VerificationRoot!;
            return new ShellRuntimePaths
            {
                IsVerification = true,
                DataDirectory = Path.GetFullPath(options.DataRoot!),
                SettingsDirectory = Path.Combine(root, "Settings"),
                LogDirectory = Path.Combine(root, "Logs"),
                ModelsDirectory = Path.Combine(root, "Models"),
                CrashDumpDirectory = Path.Combine(root, "CrashDumps"),
                DiagnosticDirectory = Path.Combine(root, "Diagnostics")
            };
        }

        var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeepVisionSharp");
        return new ShellRuntimePaths
        {
            DataDirectory = Path.Combine(local, "Data"),
            SettingsDirectory = Path.Combine(local, "Data"),
            LegacySettingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Vision.DeepVisionSharp", "settings.json"),
            LogDirectory = Path.Combine(local, "Logs"),
            // Saved models live under Data/projects/{id}/models; this is the pretrained-weights
            // folder the trainer reads, which is the only model directory at a fixed path.
            ModelsDirectory = Path.Combine(local, "Models", "pretrained"),
            CrashDumpDirectory = LocalDumpRegistration.CrashDumpDirectory,
            DiagnosticDirectory = Path.GetTempPath()
        };
    }

    internal static void SelectForProcess(ShellRuntimePaths paths)
    {
        Current = paths;
    }
}
