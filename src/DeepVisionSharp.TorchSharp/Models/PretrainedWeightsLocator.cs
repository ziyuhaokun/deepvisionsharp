namespace DeepVisionSharp.TorchSharp.Models;

/// <summary>
/// Finds the converted pretrained bundle for an architecture.
/// </summary>
/// <remarks>
/// Weights are not shipped with the installer: <c>scripts/fetch_pretrained.ps1</c> writes them under
/// <c>%LOCALAPPDATA%\DeepVisionSharp\Models\pretrained\&lt;arch&gt;</c> (or <c>DV_PRETRAINED_ROOT</c>).
/// The script uses the torchvision names (<c>resnet18</c>, <c>resnet34</c>). Training asks with the
/// internal v2 names, and ResNet-Tiny is sliced from the ResNet-18 bundle, so both names are tried.
/// A missing bundle is normal and means "train from scratch".
/// </remarks>
public static class PretrainedWeightsLocator
{
    public const string EnvironmentVariable = "DV_PRETRAINED_ROOT";

    public static string? Resolve(string archName)
    {
        if (string.IsNullOrWhiteSpace(archName))
        {
            return null;
        }

        var root = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DeepVisionSharp",
                "Models",
                "pretrained");
        }

        foreach (var candidate in CandidateNames(archName))
        {
            var directory = Path.Combine(root, candidate);
            if (File.Exists(Path.Combine(directory, "index.json")))
            {
                return directory;
            }
        }

        return null;
    }

    /// <summary>
    /// Internal architecture name first, then the directory name written by the fetch script.
    /// Tiny has no bundle of its own; its narrower stages are sliced from ResNet-18.
    /// </summary>
    private static IEnumerable<string> CandidateNames(string archName)
    {
        var normalized = archName.Trim().ToLowerInvariant();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in normalized switch
        {
            "resnet_tiny_v2" or "resnet-tiny" or "resnettiny" => new[] { "resnet18_v2", "resnet18" },
            "resnet18_v2" => new[] { "resnet18_v2", "resnet18" },
            "resnet34_v2" => new[] { "resnet34_v2", "resnet34" },
            _ => new[] { normalized }
        })
        {
            if (seen.Add(name))
            {
                yield return name;
            }
        }
    }
}
