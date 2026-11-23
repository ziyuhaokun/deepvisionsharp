using TorchSharp;
using static TorchSharp.torch;
using DeepVisionSharp.TorchSharp.Classification.Yolo26;
using DeepVisionSharp.TorchSharp.Detection.Yolo26;

namespace DeepVisionSharp.TorchSharp.Classification.Pretrained;

/// <summary>
/// Applies an Ultralytics YOLO26 weight bundle (converted from the upstream checkpoint)
/// to the hand-written TorchSharp YOLO26 models.
/// </summary>
/// <remarks>
/// Contract (shared by the trainers and the loader):
/// <list type="bullet">
/// <item>Keys in the bundle use Ultralytics names (<c>model.N...</c>).</item>
/// <item>Any tensor whose name has no counterpart or whose shape differs is skipped and reported,
/// never sliced or reshaped; the model keeps its own initialization for that tensor.</item>
/// <item>A detection model accepts either a detection bundle (<c>yolo26?</c>) or a classification
/// bundle (<c>yolo26?-cls</c>, backbone only).</item>
/// </list>
/// A classification bundle needs its own reading: Ultralytics numbers the C2PSA <c>model.9</c> in
/// <c>yolo26-cls.yaml</c> but <c>model.10</c> in <c>yolo26.yaml</c>, where 9 is the SPPF. The
/// detection head's NMS-free twins (<c>one2one_cv2</c>, <c>one2one_cv3</c>) are never applied:
/// inference and this project's loss read the <c>cv2</c>/<c>cv3</c> branch only.
/// </remarks>
public static class Yolo26PretrainedInitializer
{
    /// <summary>Graph indices shared by both checkpoints, paired with the TorchSharp module name.</summary>
    private static readonly (string Source, string Target)[] SharedModules =
    [
        ("model.0.", "backbone_0."),
        ("model.1.", "backbone_1."),
        ("model.2.", "backbone_2."),
        ("model.3.", "backbone_3."),
        ("model.4.", "backbone_4."),
        ("model.5.", "backbone_5."),
        ("model.6.", "backbone_6."),
        ("model.7.", "backbone_7."),
        ("model.8.", "backbone_8."),
        ("model.13.", "merge_p4."),
        ("model.16.", "merge_p3."),
        ("model.17.", "down_p4."),
        ("model.19.", "out_p4."),
        ("model.20.", "down_p5."),
        ("model.22.", "out_p5.")
    ];

    /// <summary>The class branch's five convolutions, keyed by their Ultralytics nesting.</summary>
    private static readonly (string Source, int Index)[] ClassBranchSteps =
    [
        ("0.0.", 0),
        ("0.1.", 1),
        ("1.0.", 2),
        ("1.1.", 3),
        ("2.", 4)
    ];

    public static PretrainedLoadResult ApplyToDetection(Yolo26DetectModel model, PretrainedWeightsBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(bundle);
        var classificationBundle = bundle.Arch.EndsWith("-cls", StringComparison.OrdinalIgnoreCase);
        return Apply(model, bundle, key => MapDetectionKey(key, classificationBundle));
    }

    public static PretrainedLoadResult ApplyToClassifier(Yolo26ClsClassifier model, PretrainedWeightsBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(bundle);
        // yolo26-cls.yaml names its layers model.0..model.10 exactly like this classifier does.
        return Apply(model, bundle, key => key);
    }

    private static PretrainedLoadResult Apply(
        torch.nn.Module model,
        PretrainedWeightsBundle bundle,
        Func<string, string?> mapKey)
    {
        var targets = model.state_dict();
        var matched = new Dictionary<string, Tensor>(StringComparer.Ordinal);
        var unexpected = new List<string>();

        foreach (var (name, source) in bundle.Tensors)
        {
            if (IsBookkeeping(name))
            {
                continue;
            }

            var mapped = mapKey(name);
            if (mapped is null)
            {
                // Deliberately unused weights (NMS-free twins, a classification head on a detector).
                continue;
            }

            if (!targets.TryGetValue(mapped, out var target) || !SameShape(source, target))
            {
                unexpected.Add(name);
                continue;
            }

            matched[mapped] = source;
        }

        using (no_grad())
        {
            foreach (var (name, source) in matched)
            {
                targets[name].copy_(source);
            }
        }

        // A module reachable both through its explicit register_module name and through its C# field
        // appears twice in state_dict with the same tensor; each tensor is counted once, and the
        // registered name is the one the report uses.
        var written = new HashSet<Tensor>(TensorReferenceComparer.Instance);
        foreach (var name in matched.Keys)
        {
            written.Add(targets[name]);
        }

        var missing = new List<string>();
        var unfilled = new HashSet<Tensor>(TensorReferenceComparer.Instance);
        foreach (var name in targets.Keys.OrderBy(key => key, Comparer<string>.Create(CompareStateDictKeys)))
        {
            if (IsBookkeeping(name) || written.Contains(targets[name]) || !unfilled.Add(targets[name]))
            {
                continue;
            }

            missing.Add(name);
        }

        return new PretrainedLoadResult(
            matched.Count,
            missing.Count,
            unexpected.Count,
            0,
            missing,
            unexpected.OrderBy(name => name, StringComparer.Ordinal).ToArray());
    }

    /// <summary>state_dict exposes each parameter under both its registered name and its C# field name.</summary>
    private sealed class TensorReferenceComparer : IEqualityComparer<Tensor>
    {
        public static readonly TensorReferenceComparer Instance = new();

        public bool Equals(Tensor? left, Tensor? right) => ReferenceEquals(left, right);

        public int GetHashCode(Tensor value) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
    }

    /// <summary>Orders registered module names before the C# field aliases pointing at the same tensor.</summary>
    private static int CompareStateDictKeys(string left, string right)
    {
        var leftAlias = left.StartsWith("_", StringComparison.Ordinal) ? 1 : 0;
        var rightAlias = right.StartsWith("_", StringComparison.Ordinal) ? 1 : 0;
        return leftAlias != rightAlias ? leftAlias.CompareTo(rightAlias) : string.CompareOrdinal(left, right);
    }

    /// <summary>
    /// Maps an Ultralytics detection key onto a TorchSharp module name, or null when the model has
    /// no counterpart. <c>model.9</c> is the SPPF in a detection checkpoint and the C2PSA in a
    /// classification one, whose <c>model.10</c> is the ImageNet head this project rebuilds.
    /// </summary>
    private static string? MapDetectionKey(string key, bool classificationBundle)
    {
        if (key.StartsWith("model.23.", StringComparison.Ordinal))
        {
            return MapDetectHead(key);
        }

        if (key.StartsWith("model.9.", StringComparison.Ordinal))
        {
            return (classificationBundle ? "psa." : "sppf.") + key["model.9.".Length..];
        }

        if (key.StartsWith("model.10.", StringComparison.Ordinal))
        {
            return classificationBundle ? null : "psa." + key["model.10.".Length..];
        }

        foreach (var (source, target) in SharedModules)
        {
            if (key.StartsWith(source, StringComparison.Ordinal))
            {
                return target + key[source.Length..];
            }
        }

        return null;
    }

    /// <summary>Maps <c>model.23.cv2|</c>cv3.<c>{scale}</c> onto <c>head_p{3..5}.box|cls</c>.</summary>
    private static string? MapDetectHead(string key)
    {
        var rest = key["model.23.".Length..];
        string family;
        if (rest.StartsWith("cv2.", StringComparison.Ordinal))
        {
            family = "box";
        }
        else if (rest.StartsWith("cv3.", StringComparison.Ordinal))
        {
            family = "cls";
        }
        else
        {
            return null;
        }

        var tail = rest[4..];
        var scale = tail[0] switch { '0' => 3, '1' => 4, '2' => 5, _ => 0 };
        if (scale == 0 || tail.Length < 2 || tail[1] != '.')
        {
            return null;
        }

        tail = tail[2..];
        if (family == "box")
        {
            return $"head_p{scale}.box.{tail}";
        }

        return MapClassBranch(tail) is { } step ? $"head_p{scale}.cls.{step}" : null;
    }

    private static string? MapClassBranch(string tail)
    {
        foreach (var (source, index) in ClassBranchSteps)
        {
            if (tail.StartsWith(source, StringComparison.Ordinal))
            {
                return $"{index}.{tail[source.Length..]}";
            }
        }

        return null;
    }

    /// <summary>BatchNorm's step counter carries no weights; the model keeps its own.</summary>
    private static bool IsBookkeeping(string key) =>
        key.EndsWith(".num_batches_tracked", StringComparison.Ordinal);

    private static bool SameShape(Tensor source, Tensor target)
    {
        var expected = source.shape;
        var actual = target.shape;
        if (expected.Length != actual.Length)
        {
            return false;
        }

        for (var index = 0; index < expected.Length; index++)
        {
            if (expected[index] != actual[index])
            {
                return false;
            }
        }

        return true;
    }
}
