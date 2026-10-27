namespace RuntimePackage.Engine.Installation;

public sealed record InstallPreflightResult(
    bool IsValid,
    string ResolvedInstallDirectory,
    string PrimaryEntryPointRelativePath,
    bool RequiresOverwriteConfirmation,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Notes);
