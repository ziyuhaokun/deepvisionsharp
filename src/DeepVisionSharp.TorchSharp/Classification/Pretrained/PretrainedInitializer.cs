using System.Text.RegularExpressions;

using TorchSharp;
using TorchSharp.Modules;
using static TorchSharp.torch;

namespace DeepVisionSharp.TorchSharp.Classification.Pretrained;

/// <summary>Outcome of applying a pretrained bundle to a model.</summary>
public sealed record PretrainedLoadResult(
    int Loaded,
    int Missing,
    int Unexpected,
    int Sliced,
    IReadOnlyList<string> MissingKeys,
    IReadOnlyList<string> UnexpectedKeys);

/// <summary>
/// Applies torchvision weights to a <see cref="ResNet.TorchVisionResNetClassifier"/>.
/// </summary>
/// <remarks>
/// Full-width models load every key except the classifier head. The nano model is a width-reduced
/// ResNet18, so its weights are sliced: per stage the strongest filters (by L1 norm) are kept, and
/// the next layer's input channels are sliced with the same index set so shapes and semantics stay
/// aligned. Sliced transfer is weaker than full fine-tuning but far better than random init.
/// </remarks>
public static class PretrainedInitializer
{
    private static readonly Regex LayerPattern = new(@"^layer(?<stage>\d)\.(?<block>\d+)\.", RegexOptions.Compiled);

    public static PretrainedLoadResult Apply(
        torch.nn.Module<torch.Tensor, torch.Tensor> model,
        PretrainedWeightsBundle bundle,
        IReadOnlyList<int> targetWidths)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(bundle);
        if (targetWidths.Count != 5)
        {
            throw new ArgumentException("targetWidths must be [stem, layer1, layer2, layer3, layer4].", nameof(targetWidths));
        }

        var baseWidths = ReadBaseWidths(bundle);
        var selected = BuildSelections(bundle, baseWidths, targetWidths);
        var modelKeys = model.state_dict().Keys.ToHashSet(StringComparer.Ordinal);
        var prepared = new Dictionary<string, Tensor>(StringComparer.Ordinal);
        var owned = new List<Tensor>();
        var sliced = 0;

        try
        {
            foreach (var (name, source) in bundle.Tensors)
            {
                if (name.StartsWith("fc.", StringComparison.Ordinal)
                    || !modelKeys.Contains(name))
                {
                    // The classifier head is re-initialized for the project's class count; keys the
                    // model does not expose (e.g. num_batches_tracked) are simply ignored.
                    continue;
                }

                var (outStage, inStage) = ResolveStages(name);
                if (outStage is null)
                {
                    prepared[name] = source;
                    continue;
                }

                var tensor = Slice(source, outStage.Value, inStage, selected, owned);
                if (!ReferenceEquals(tensor, source))
                {
                    sliced++;
                }

                prepared[name] = tensor;
            }

            var (missing, unexpected) = model.load_state_dict(prepared, strict: false);
            // The classifier head is intentionally random, so it is not a "missing" weight.
            var missingBackbone = missing.Count(name =>
                !name.StartsWith("fc.", StringComparison.Ordinal)
                && !name.EndsWith(".num_batches_tracked", StringComparison.Ordinal));
            return new PretrainedLoadResult(
                prepared.Count,
                missingBackbone,
                unexpected.Count,
                sliced,
                missing.Where(name =>
                        !name.StartsWith("fc.", StringComparison.Ordinal)
                        && !name.EndsWith(".num_batches_tracked", StringComparison.Ordinal))
                    .ToArray(),
                unexpected.ToArray());
        }
        finally
        {
            foreach (var tensor in owned)
            {
                tensor.Dispose();
            }
        }
    }

    private static int[] ReadBaseWidths(PretrainedWeightsBundle bundle)
    {
        var widths = new int[5];
        widths[0] = (int)bundle.Tensors["conv1.weight"].shape[0];
        for (var stage = 1; stage <= 4; stage++)
        {
            widths[stage] = (int)bundle.Tensors[$"layer{stage}.0.conv1.weight"].shape[0];
        }

        return widths;
    }

    private static int[][] BuildSelections(
        PretrainedWeightsBundle bundle,
        int[] baseWidths,
        IReadOnlyList<int> targetWidths)
    {
        var selected = new int[5][];
        for (var stage = 0; stage < 5; stage++)
        {
            var target = targetWidths[stage];
            if (target <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(targetWidths), "stage widths must be positive");
            }

            if (target > baseWidths[stage])
            {
                throw new InvalidOperationException(
                    $"目标宽度 {target} 超过预训练权重宽度 {baseWidths[stage]}（stage {stage}）。");
            }

            var weight = stage == 0
                ? bundle.Tensors["conv1.weight"]
                : bundle.Tensors[$"layer{stage}.0.conv1.weight"];
            selected[stage] = SelectChannels(weight, target);
        }

        return selected;
    }

    private static int[] SelectChannels(Tensor weight, int target)
    {
        var outChannels = (int)weight.shape[0];
        if (target >= outChannels)
        {
            return Enumerable.Range(0, outChannels).ToArray();
        }

        // Copy before reshaping so disposing the temporary cannot touch the bundle's storage.
        using var copy = weight.detach().cpu().clone();
        using var flat = copy.reshape(outChannels, -1).abs();
        using var sums = flat.sum(1);
        var values = sums.data<float>().ToArray();
        return Enumerable.Range(0, outChannels)
            .OrderByDescending(index => values[index])
            .Take(target)
            .OrderBy(index => index)
            .ToArray();
    }

    /// <summary>Maps a torchvision key to (output stage, input stage); stages are 0=stem, 1..4=layers.</summary>
    private static (int? OutStage, int? InStage) ResolveStages(string name)
    {
        if (name.StartsWith("conv1.", StringComparison.Ordinal) || name.StartsWith("bn1.", StringComparison.Ordinal))
        {
            return (0, null);
        }

        var match = LayerPattern.Match(name);
        if (!match.Success)
        {
            return (null, null);
        }

        var stage = int.Parse(match.Groups["stage"].Value);
        var block = int.Parse(match.Groups["block"].Value);
        var remainder = name[match.Length..];

        if (remainder.StartsWith("conv1.", StringComparison.Ordinal))
        {
            return (stage, block == 0 ? stage - 1 : stage);
        }

        if (remainder.StartsWith("conv2.", StringComparison.Ordinal))
        {
            return (stage, stage);
        }

        if (remainder.StartsWith("downsample.0.", StringComparison.Ordinal))
        {
            return (stage, stage - 1);
        }

        // bn1/bn2/downsample.1 are per-output-channel vectors; scalars (num_batches_tracked) are kept.
        return (stage, null);
    }

    private static Tensor Slice(
        Tensor source,
        int outStage,
        int? inStage,
        int[][] selected,
        List<Tensor> owned)
    {
        var dims = source.shape;
        if (dims.Length == 0)
        {
            return source;
        }

        var result = source;
        var outIndices = selected[outStage];
        if (outIndices.Length != dims[0])
        {
            using var index = tensor(outIndices, dtype: ScalarType.Int64);
            result = result.index_select(0, index);
            owned.Add(result);
        }

        if (inStage is int stage && dims.Length >= 2)
        {
            var inIndices = selected[stage];
            if (inIndices.Length != dims[1])
            {
                using var index = tensor(inIndices, dtype: ScalarType.Int64);
                result = result.index_select(1, index);
                owned.Add(result);
            }
        }

        return result;
    }
}


