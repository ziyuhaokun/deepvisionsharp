namespace RuntimePackage.Engine.Packaging;

public enum PackagerOutcome
{
    Success,
    Failure,
    Cancelled
}

public sealed record PackagerResult(
    PackagerOutcome Outcome,
    string PackagePath,
    string PayloadSha256,
    long PayloadSizeBytes,
    string LogFilePath,
    bool CleanupCompleted,
    string SummaryMessage)
{
    public bool Success => Outcome == PackagerOutcome.Success;
}
