namespace DeepVisionSharp.TorchSharp.Models;

/// <summary>
/// Finds the converted pretrained bundle for an architecture.
/// </summary>
/// <remarks>
/// Weights are not shipped with the installer: <c>scripts/fetch_pretrained.ps1</c> writes them under
/// <c>%LOCALAPPDATA%\DeepVisionSharp\Models\pretrained\&lt;arch&gt;</c> (or <c>DV_PRETRAINED_ROOT</c>).
/// The script uses the torchvision names (<c>resnet18</c>, <c>resnet34</c>) and the Ultralytics ones
/// (<c>yolo26n</c>, <c>yolo26n-cls</c>). Training asks with the internal v2 names, and ResNet-Tiny is
/// sliced from the ResNet-18 bundle, so both names are tried. A missing bundle is normal and means
/// "train from scratch".
/// </remarks>
public static class PretrainedWeightsLocator
{
    public const string EnvironmentVariable = "DV_PRETRAINED_ROOT";

    /// <summary>Where bundles live: <c>DV_PRETRAINED_ROOT</c> when set, else the per-user models folder.</summary>
    public static string RootDirectory
    {
        get
        {
            var root = Environment.GetEnvironmentVariable(EnvironmentVariable);
            return string.IsNullOrWhiteSpace(root)
                ? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "DeepVisionSharp",
                    "Models",
                    "pretrained")
                : root;
        }
    }

    public static string? Resolve(string archName)
    {
        if (string.IsNullOrWhiteSpace(archName))
        {
            return null;
        }

        var root = RootDirectory;

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
        var candidates = TryResolveYolo26(normalized, out var scale, out var classifier)
            ? classifier
                ? new[] { $"yolo26{scale}-cls" }
                // A detection model can also start from the classification bundle, which carries the
                // same backbone; only the head is missing there.
                : new[] { $"yolo26{scale}", $"yolo26{scale}-cls" }
            : normalized switch
            {
                "resnet_tiny_v2" or "resnet-tiny" or "resnettiny" => new[] { "resnet18_v2", "resnet18" },
                "resnet18_v2" => new[] { "resnet18_v2", "resnet18" },
                "resnet34_v2" => new[] { "resnet34_v2", "resnet34" },
                _ => new[] { normalized }
            };

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in candidates)
        {
            if (seen.Add(name))
            {
                yield return name;
            }
        }
    }

    /// <summary>
    /// Recognises the YOLO26 names the trainers pass (<c>yolo26n-cls</c>, <c>yolo26-n-det</c>) and the
    /// bundle directories the fetch script writes (<c>yolo26n</c>, <c>yolo26n-cls</c>).
    /// </summary>
    private static bool TryResolveYolo26(string normalized, out char scale, out bool classifier)
    {
        scale = default;
        classifier = false;
        var name = normalized.Replace('_', '-');
        if (!name.StartsWith("yolo26", StringComparison.Ordinal))
        {
            return false;
        }

        var tail = name["yolo26".Length..];
        foreach (var suffix in new[] { "-cls", "-det" })
        {
            if (tail.EndsWith(suffix, StringComparison.Ordinal))
            {
                classifier = suffix == "-cls";
                tail = tail[..^suffix.Length];
                break;
            }
        }

        tail = tail.TrimStart('-');
        if (tail.Length != 1 || "nsmlx".IndexOf(tail[0]) < 0)
        {
            return false;
        }

        scale = tail[0];
        return true;
    }
}
