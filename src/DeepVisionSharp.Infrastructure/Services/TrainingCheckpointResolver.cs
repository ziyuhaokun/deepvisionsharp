namespace DeepVisionSharp.Infrastructure.Services;

internal static class TrainingCheckpointResolver
{
    private static readonly string[] BestRelativePaths =
    [
        Path.Combine("train", "weights", "best.pt"),
        "best.torchsharp",
        "best.pt",
        "best.ckpt"
    ];

    private static readonly string[] LastRelativePaths =
    [
        Path.Combine("train", "weights", "last.pt"),
        "last.torchsharp",
        "last.pt"
    ];

    public static string? ResolveBestFromOutputDirectory(string? outputDir)
        => FirstExisting(outputDir, BestRelativePaths);

    public static string? ResolveLastFromOutputDirectory(string? outputDir)
        => FirstExisting(outputDir, LastRelativePaths);

    private static string? FirstExisting(string? outputDir, IReadOnlyList<string> relativePaths)
    {
        if (string.IsNullOrWhiteSpace(outputDir) || !Directory.Exists(outputDir))
        {
            return null;
        }

        foreach (var relative in relativePaths)
        {
            var candidate = Path.Combine(outputDir, relative);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
