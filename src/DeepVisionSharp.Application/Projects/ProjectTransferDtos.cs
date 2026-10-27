namespace DeepVisionSharp.Application.Projects;

public sealed class ProjectTransferResult
{
    public ProjectTransferResult(bool succeeded, int total, int added, int failed, int skipped, Guid? projectId = null)
    {
        Succeeded = succeeded;
        Total = total;
        Added = added;
        Failed = failed;
        Skipped = skipped;
        ProjectId = projectId;
    }

    public bool Succeeded { get; }

    public int Total { get; }

    public int Added { get; }

    public int Failed { get; }

    public int Skipped { get; }

    public Guid? ProjectId { get; }

    public List<string> Errors { get; } = new();
}

public sealed class ProjectTransferProgress
{
    public ProjectTransferProgress(string stage, int processed, int total, int added, string? currentFile = null)
    {
        Stage = stage;
        Processed = processed;
        Total = total;
        Added = added;
        CurrentFile = currentFile;
    }

    public string Stage { get; }

    public int Processed { get; }

    public int Total { get; }

    public int Added { get; }

    public string? CurrentFile { get; }
}
