using TorchSharp;
using TorchSharp.Modules;
using static TorchSharp.torch;

namespace DeepVisionSharp.TorchSharp.Training;

/// <summary>
/// Optimizer construction and the iteration-based learning-rate schedule shared by the detection and
/// classification trainers.
/// </summary>
/// <remarks>
/// <para>
/// Parameters are split the way Ultralytics splits them: conv/linear weights carry weight decay,
/// normalisation weights and every bias do not. A bias-heavy head would otherwise be regularised
/// towards zero for no accuracy gain.
/// </para>
/// <para>
/// TorchSharp's <c>CosineAnnealingLR</c> steps once per call and applies one learning rate to the
/// whole optimizer, so it cannot express a warmup that only lifts the bias group. The schedule is
/// therefore computed here as pure functions and written into the parameter groups before each
/// optimizer step; TorchSharp 0.107 re-reads each group's learning rate and momentum on every
/// <c>step()</c>, so this actually takes effect (verified against the CPU backend).
/// </para>
/// </remarks>
public static class TrainingOptimization
{
    /// <summary>Weight decay for conv/linear weights; norms and biases are excluded.</summary>
    public const double WeightDecay = 5e-4;

    /// <summary>SGD momentum, and the value it warms up from over the warmup iterations.</summary>
    public const double SgdMomentum = 0.937;
    public const double SgdWarmupMomentum = 0.8;

    /// <summary>
    /// Absolute learning rate the bias group starts from during warmup, so the head can move before
    /// the backbone does (Ultralytics' <c>warmup_bias_lr</c>).
    /// </summary>
    public const double WarmupBiasLearningRate = 0.1;

    /// <summary>The cosine tail ends at this fraction of lr0.</summary>
    public const double FinalLearningRateFactor = 0.01;

    /// <summary>Adam/AdamW first and second moment decay.</summary>
    public const double AdamBeta1 = 0.937;
    public const double AdamBeta2 = 0.999;

    /// <summary>Warmup lasts three epochs' worth of iterations, never less than this.</summary>
    private const int MinimumWarmupIterations = 100;

    /// <summary>...and never more than this share of the run.</summary>
    private const double WarmupIterationShare = 0.3;

    /// <summary>
    /// <c>min(max(3 * iterationsPerEpoch, 100), 0.3 * totalIterations)</c>. A two-epoch run can shrink
    /// the warmup to nothing, which is intended: a short run has no room for one.
    /// </summary>
    public static int WarmupIterations(int iterationsPerEpoch, int epochs)
    {
        var perEpoch = Math.Max(1, iterationsPerEpoch);
        var total = perEpoch * Math.Max(1, epochs);
        var warmup = Math.Min(
            Math.Max(3 * perEpoch, MinimumWarmupIterations),
            (int)(total * WarmupIterationShare));
        return Math.Max(0, warmup);
    }

    /// <summary>
    /// Learning rate for the decay and norm groups at a 0-based optimizer step: linear from 0 to lr0
    /// over the warmup, then cosine down to <see cref="FinalLearningRateFactor"/> * lr0.
    /// </summary>
    public static double LearningRate(
        int iteration,
        int warmupIterations,
        int totalIterations,
        double baseLearningRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(baseLearningRate);

        if (warmupIterations > 0 && iteration < warmupIterations)
        {
            return baseLearningRate * iteration / warmupIterations;
        }

        var decayIterations = Math.Max(1, totalIterations - warmupIterations);
        var progress = Math.Clamp((iteration - warmupIterations) / (double)decayIterations, 0d, 1d);
        var final = baseLearningRate * FinalLearningRateFactor;
        return final + ((baseLearningRate - final) * 0.5 * (1 + Math.Cos(Math.PI * progress)));
    }

    /// <summary>
    /// Learning rate for the bias group: from <see cref="WarmupBiasLearningRate"/> down to the
    /// scheduled value over the warmup, then identical to <see cref="LearningRate"/>.
    /// </summary>
    public static double BiasLearningRate(
        int iteration,
        int warmupIterations,
        int totalIterations,
        double baseLearningRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(baseLearningRate);

        if (warmupIterations > 0 && iteration < warmupIterations)
        {
            var progress = iteration / (double)warmupIterations;
            return WarmupBiasLearningRate + ((baseLearningRate - WarmupBiasLearningRate) * progress);
        }

        return LearningRate(iteration, warmupIterations, totalIterations, baseLearningRate);
    }

    /// <summary>SGD momentum ramps from <see cref="SgdWarmupMomentum"/> to <see cref="SgdMomentum"/>.</summary>
    public static double Momentum(int iteration, int warmupIterations)
    {
        if (warmupIterations <= 0 || iteration >= warmupIterations)
        {
            return SgdMomentum;
        }

        return SgdWarmupMomentum
            + ((SgdMomentum - SgdWarmupMomentum) * iteration / (double)warmupIterations);
    }

    /// <summary>
    /// Detection checkpoint quality: mAP50-95 dominates, so a model that only fires on easy boxes
    /// still loses to one that localises. mAP50 alone sat at 0 for whole runs and pinned "best" to
    /// the first epoch.
    /// </summary>
    public static double DetectionFitness(double map50, double map50_95) =>
        (0.1 * map50) + (0.9 * map50_95);

    /// <summary>Splits parameters into decayed weights, normalisation weights and biases.</summary>
    public sealed record ParameterGroups(
        IReadOnlyList<Parameter> DecayWeights,
        IReadOnlyList<Parameter> NormWeights,
        IReadOnlyList<Parameter> Biases);

    /// <summary>
    /// Groups a model's parameters by owning module type: <c>Conv2d</c>/<c>Linear</c> weights decay,
    /// everything else (BatchNorm/LayerNorm scales, every bias, anything unattributable) does not.
    /// </summary>
    public static ParameterGroups GroupParameters(nn.Module model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var moduleTypes = new Dictionary<string, Type>(StringComparer.Ordinal);
        foreach (var (name, module) in model.named_modules())
        {
            if (name.Length > 0)
            {
                moduleTypes[name] = module.GetType();
            }
        }

        const string weightSuffix = ".weight";
        const string biasSuffix = ".bias";
        var decayWeights = new List<Parameter>();
        var normWeights = new List<Parameter>();
        var biases = new List<Parameter>();
        foreach (var (name, parameter) in model.named_parameters())
        {
            if (name.EndsWith(biasSuffix, StringComparison.Ordinal))
            {
                biases.Add(parameter);
                continue;
            }

            if (name.EndsWith(weightSuffix, StringComparison.Ordinal))
            {
                var owner = name[..^weightSuffix.Length];
                if (moduleTypes.TryGetValue(owner, out var ownerType)
                    && (ownerType == typeof(Conv2d)
                        || ownerType == typeof(Linear)))
                {
                    decayWeights.Add(parameter);
                    continue;
                }
            }

            normWeights.Add(parameter);
        }

        return new ParameterGroups(decayWeights, normWeights, biases);
    }
}

/// <summary>
/// The optimizer plus the per-iteration schedule the trainers drive it with.
/// </summary>
/// <remarks>
/// TorchSharp 0.107 exposes per-group options through the nested <c>ParamGroup</c> types and honours
/// learning-rate and momentum changes made between steps, so the warmup, the cosine tail and the
/// momentum ramp are all applied without rebuilding the optimizer.
/// </remarks>
public sealed class TrainingOptimizer : IDisposable
{
    private readonly optim.Optimizer _optimizer;
    private readonly ParamGroup? _decayGroup;
    private readonly ParamGroup? _normGroup;
    private readonly ParamGroup? _biasGroup;
    private readonly SGD.ParamGroup[]? _sgdGroups;
    private readonly double _baseLearningRate;

    private TrainingOptimizer(
        optim.Optimizer optimizer,
        ParamGroup? decayGroup,
        ParamGroup? normGroup,
        ParamGroup? biasGroup,
        SGD.ParamGroup[]? sgdGroups,
        double baseLearningRate)
    {
        _optimizer = optimizer;
        _decayGroup = decayGroup;
        _normGroup = normGroup;
        _biasGroup = biasGroup;
        _sgdGroups = sgdGroups;
        _baseLearningRate = baseLearningRate;
    }

    public optim.Optimizer Optimizer => _optimizer;

    /// <summary>False for Adam/AdamW, whose betas are not a warmup knob here.</summary>
    public bool SupportsMomentumSchedule => _sgdGroups is not null;

    /// <summary>Learning rate the last <see cref="ApplySchedule"/> applied to the backbone groups.</summary>
    public double LastLearningRate { get; private set; }

    public static TrainingOptimizer Create(
        string optimizerName,
        TrainingOptimization.ParameterGroups groups,
        double learningRate)
    {
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(learningRate);

        switch (optimizerName?.Trim().ToLowerInvariant())
        {
            case "sgd":
            {
                var sgdGroups = new[]
                {
                    new SGD.ParamGroup(
                        groups.DecayWeights,
                        lr: learningRate,
                        momentum: TrainingOptimization.SgdMomentum,
                        weight_decay: TrainingOptimization.WeightDecay,
                        nesterov: true),
                    new SGD.ParamGroup(
                        groups.NormWeights,
                        lr: learningRate,
                        momentum: TrainingOptimization.SgdMomentum,
                        weight_decay: 0,
                        nesterov: true),
                    new SGD.ParamGroup(
                        groups.Biases,
                        lr: learningRate,
                        momentum: TrainingOptimization.SgdMomentum,
                        weight_decay: 0,
                        nesterov: true)
                };
                var optimizer = optim.SGD(
                    sgdGroups,
                    learningRate,
                    TrainingOptimization.SgdMomentum,
                    dampening: 0,
                    weight_decay: 0,
                    nesterov: true);
                return new TrainingOptimizer(
                    optimizer, sgdGroups[0], sgdGroups[1], sgdGroups[2], sgdGroups, learningRate);
            }

            case "adam":
            {
                // Plain Adam keeps its L2 term off: the decay group idea is a decoupled-weight-decay
                // one, and Adam's L2 form interacts badly with adaptive scaling.
                var adamGroups = new[]
                {
                    new Adam.ParamGroup(
                        groups.DecayWeights,
                        lr: learningRate,
                        beta1: TrainingOptimization.AdamBeta1,
                        beta2: TrainingOptimization.AdamBeta2),
                    new Adam.ParamGroup(
                        groups.NormWeights,
                        lr: learningRate,
                        beta1: TrainingOptimization.AdamBeta1,
                        beta2: TrainingOptimization.AdamBeta2),
                    new Adam.ParamGroup(
                        groups.Biases,
                        lr: learningRate,
                        beta1: TrainingOptimization.AdamBeta1,
                        beta2: TrainingOptimization.AdamBeta2)
                };
                var optimizer = optim.Adam(
                    adamGroups, learningRate, TrainingOptimization.AdamBeta1, TrainingOptimization.AdamBeta2);
                return new TrainingOptimizer(
                    optimizer, adamGroups[0], adamGroups[1], adamGroups[2], null, learningRate);
            }

            case "adamw":
            {
                var adamwGroups = new[]
                {
                    new AdamW.ParamGroup(
                        groups.DecayWeights,
                        lr: learningRate,
                        beta1: TrainingOptimization.AdamBeta1,
                        beta2: TrainingOptimization.AdamBeta2,
                        weight_decay: TrainingOptimization.WeightDecay),
                    new AdamW.ParamGroup(
                        groups.NormWeights,
                        lr: learningRate,
                        beta1: TrainingOptimization.AdamBeta1,
                        beta2: TrainingOptimization.AdamBeta2),
                    new AdamW.ParamGroup(
                        groups.Biases,
                        lr: learningRate,
                        beta1: TrainingOptimization.AdamBeta1,
                        beta2: TrainingOptimization.AdamBeta2)
                };
                var optimizer = optim.AdamW(
                    adamwGroups, learningRate, TrainingOptimization.AdamBeta1, TrainingOptimization.AdamBeta2);
                return new TrainingOptimizer(
                    optimizer, adamwGroups[0], adamwGroups[1], adamwGroups[2], null, learningRate);
            }

            default:
                throw new ArgumentException($"不支持的优化器：{optimizerName}", nameof(optimizerName));
        }
    }

    /// <summary>Writes the learning rates and momentum for one 0-based iteration into the groups.</summary>
    public void ApplySchedule(int iteration, int warmupIterations, int totalIterations)
    {
        var learningRate = TrainingOptimization.LearningRate(
            iteration, warmupIterations, totalIterations, _baseLearningRate);
        var biasLearningRate = TrainingOptimization.BiasLearningRate(
            iteration, warmupIterations, totalIterations, _baseLearningRate);

        SetLearningRate(_decayGroup, learningRate);
        SetLearningRate(_normGroup, learningRate);
        SetLearningRate(_biasGroup, biasLearningRate);
        if (_sgdGroups is not null)
        {
            var momentum = TrainingOptimization.Momentum(iteration, warmupIterations);
            foreach (var group in _sgdGroups)
            {
                group.Momentum = momentum;
            }
        }

        LastLearningRate = learningRate;
    }

    private static void SetLearningRate(ParamGroup? group, double learningRate)
    {
        if (group is not null)
        {
            group.LearningRate = learningRate;
        }
    }

    public void Dispose() => _optimizer.Dispose();
}

/// <summary>
/// Keeps the detection epoch with the best <see cref="TrainingOptimization.DetectionFitness"/>.
/// </summary>
/// <remarks>
/// The comparison is <c>&gt;=</c> on purpose: when nothing learns and fitness stays at 0, every epoch
/// is "as good as" the best, so the last one wins instead of the first.
/// </remarks>
public sealed class DetectionBestEpochTracker
{
    public double BestFitness { get; private set; } = -1;

    public int BestEpoch { get; private set; }

    /// <summary>Records an epoch; returns true when its checkpoint should replace the current best.</summary>
    public bool Report(int epoch, double map50, double map50_95)
    {
        var fitness = TrainingOptimization.DetectionFitness(map50, map50_95);
        if (fitness < BestFitness)
        {
            return false;
        }

        BestFitness = fitness;
        BestEpoch = epoch;
        return true;
    }
}
