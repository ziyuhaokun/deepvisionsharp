using TorchSharp;
using TorchSharp.Modules;
using Xunit;
using DeepVisionSharp.TorchSharp.Training;
using static TorchSharp.torch;
using static TorchSharp.torch.nn;

namespace DeepVisionSharp.TorchSharp.Tests;

/// <summary>
/// The optimizer grouping and the iteration-based schedule both trainers use. The numbers are
/// derived from the intended shape (linear warmup, cosine tail, momentum ramp) rather than from the
/// implementation, so a change to the formula fails here.
/// </summary>
public sealed class TrainingOptimizationTests
{
    [Fact]
    public void GroupParameters_decays_conv_and_linear_weights_but_not_norms_or_biases()
    {
        using var model = new TinyNet();

        var groups = TrainingOptimization.GroupParameters(model);

        Assert.Equal(
            new[] { "conv.weight", "head.weight" },
            NamesOf(groups.DecayWeights, model).OrderBy(name => name, StringComparer.Ordinal));
        Assert.Equal(new[] { "norm.weight" }, NamesOf(groups.NormWeights, model));
        Assert.Equal(
            new[] { "conv.bias", "head.bias", "norm.bias" },
            NamesOf(groups.Biases, model).OrderBy(name => name, StringComparer.Ordinal));
        // Every parameter lands in exactly one group; a dropped one silently stops training.
        Assert.Equal(6, groups.DecayWeights.Count + groups.NormWeights.Count + groups.Biases.Count);
    }

    [Fact]
    public void WarmupIterations_is_three_epochs_but_capped_by_thirty_percent_of_the_run()
    {
        // 5 iterations/epoch * 3 epochs = 15 iterations: the 100 floor wins, then the 30% cap.
        Assert.Equal(4, TrainingOptimization.WarmupIterations(5, 3));
        // 100 * 10 = 1000 iterations: three epochs (300) fits inside the 30% cap.
        Assert.Equal(300, TrainingOptimization.WarmupIterations(100, 10));
        // 50 * 2 = 100 iterations: 150 wanted, 30 allowed.
        Assert.Equal(30, TrainingOptimization.WarmupIterations(50, 2));
        // Two iterations total cannot host a warmup at all.
        Assert.Equal(0, TrainingOptimization.WarmupIterations(2, 1));
    }

    [Fact]
    public void LearningRate_rises_linearly_during_warmup_then_cosine_decays_to_one_percent()
    {
        const double baseRate = 0.1;
        const int warmup = 10;
        const int total = 100;

        Assert.Equal(0d, TrainingOptimization.LearningRate(0, warmup, total, baseRate), 9);
        Assert.Equal(baseRate / 2, TrainingOptimization.LearningRate(5, warmup, total, baseRate), 9);
        Assert.Equal(baseRate, TrainingOptimization.LearningRate(warmup, warmup, total, baseRate), 9);
        // Halfway through the cosine the rate is the mean of lr0 and the tail.
        Assert.Equal(
            (baseRate + (baseRate * 0.01)) / 2,
            TrainingOptimization.LearningRate(55, warmup, total, baseRate),
            9);
        Assert.Equal(baseRate * 0.01, TrainingOptimization.LearningRate(total, warmup, total, baseRate), 9);

        var previous = double.MaxValue;
        for (var iteration = warmup; iteration <= total; iteration++)
        {
            var rate = TrainingOptimization.LearningRate(iteration, warmup, total, baseRate);
            Assert.True(rate <= previous + 1e-12, $"iteration {iteration}: {rate} > {previous}");
            previous = rate;
        }

        // No warmup requested: the cosine starts at lr0.
        Assert.Equal(baseRate, TrainingOptimization.LearningRate(0, 0, total, baseRate), 9);
    }

    [Fact]
    public void BiasLearningRate_starts_at_the_warmup_bias_rate_and_joins_the_schedule()
    {
        const double baseRate = 0.01;
        const int warmup = 10;
        const int total = 100;

        // The bias group starts high (absolute, not a multiple of lr0) so the head can move first.
        Assert.Equal(
            TrainingOptimization.WarmupBiasLearningRate,
            TrainingOptimization.BiasLearningRate(0, warmup, total, baseRate),
            9);
        Assert.Equal(
            (TrainingOptimization.WarmupBiasLearningRate + baseRate) / 2,
            TrainingOptimization.BiasLearningRate(5, warmup, total, baseRate),
            9);
        // At the end of the warmup both groups agree, and stay together afterwards.
        Assert.Equal(
            baseRate,
            TrainingOptimization.BiasLearningRate(warmup, warmup, total, baseRate),
            9);
        for (var iteration = warmup; iteration <= total; iteration += 10)
        {
            Assert.Equal(
                TrainingOptimization.LearningRate(iteration, warmup, total, baseRate),
                TrainingOptimization.BiasLearningRate(iteration, warmup, total, baseRate),
                9);
        }
    }

    [Fact]
    public void Momentum_ramps_from_0_8_to_0_937_over_the_warmup()
    {
        Assert.Equal(
            TrainingOptimization.SgdWarmupMomentum,
            TrainingOptimization.Momentum(0, 10),
            9);
        Assert.Equal(0.8685, TrainingOptimization.Momentum(5, 10), 9);
        Assert.Equal(TrainingOptimization.SgdMomentum, TrainingOptimization.Momentum(10, 10), 9);
        Assert.Equal(TrainingOptimization.SgdMomentum, TrainingOptimization.Momentum(0, 0), 9);
    }

    [Fact]
    public void TrainingOptimizer_writes_different_warmup_rates_into_bias_and_momentum_groups()
    {
        using var model = new TinyNet();
        var groups = TrainingOptimization.GroupParameters(model);
        using var optimizer = TrainingOptimizer.Create("sgd", groups, learningRate: 0.01);

        Assert.True(optimizer.SupportsMomentumSchedule);
        var paramGroups = optimizer.Optimizer.ParamGroups.ToArray();
        Assert.Equal(3, paramGroups.Length);

        optimizer.ApplySchedule(iteration: 0, warmupIterations: 10, totalIterations: 100);
        Assert.Equal(0d, paramGroups[0].LearningRate, 9);
        Assert.Equal(0d, paramGroups[1].LearningRate, 9);
        Assert.Equal(TrainingOptimization.WarmupBiasLearningRate, paramGroups[2].LearningRate, 9);
        Assert.Equal(TrainingOptimization.SgdWarmupMomentum, ((SGD.ParamGroup)paramGroups[0]).Momentum, 9);

        optimizer.ApplySchedule(iteration: 10, warmupIterations: 10, totalIterations: 100);
        Assert.Equal(0.01, paramGroups[0].LearningRate, 9);
        Assert.Equal(0.01, paramGroups[2].LearningRate, 9);
        Assert.Equal(TrainingOptimization.SgdMomentum, ((SGD.ParamGroup)paramGroups[0]).Momentum, 9);

        using var adam = TrainingOptimizer.Create("adamw", groups, learningRate: 0.01);
        Assert.False(adam.SupportsMomentumSchedule);
        Assert.Equal(3, adam.Optimizer.ParamGroups.Count());
    }

    [Fact]
    public void DetectionBestEpochTracker_keeps_the_last_epoch_when_nothing_learns()
    {
        var tracker = new DetectionBestEpochTracker();

        // mAP stays 0 for the whole run: '>=' keeps moving the best epoch forward, so the checkpoint
        // is the last epoch instead of the first (which '>' produced before any box was learned).
        Assert.True(tracker.Report(1, map50: 0, map50_95: 0));
        Assert.True(tracker.Report(2, map50: 0, map50_95: 0));
        Assert.True(tracker.Report(3, map50: 0, map50_95: 0));

        Assert.Equal(3, tracker.BestEpoch);
        Assert.Equal(0d, tracker.BestFitness, 9);

        // mAP50-95 outweighs mAP50, and a worse epoch never displaces the best one.
        Assert.True(tracker.Report(4, map50: 0.5, map50_95: 0.2));
        Assert.Equal(4, tracker.BestEpoch);
        Assert.False(tracker.Report(5, map50: 0.9, map50_95: 0.1));
        Assert.Equal(4, tracker.BestEpoch);
        Assert.Equal(TrainingOptimization.DetectionFitness(0.5, 0.2), tracker.BestFitness, 9);
    }

    private static IReadOnlyList<string> NamesOf(
        IReadOnlyList<Parameter> parameters,
        TinyNet model)
    {
        var names = new List<string>();
        foreach (var name in new[] { "conv.weight", "conv.bias", "norm.weight", "norm.bias", "head.weight", "head.bias" })
        {
            var parameter = model.get_parameter(name);
            if (parameters.Any(candidate => ReferenceEquals(candidate, parameter)))
            {
                names.Add(name);
            }
        }

        return names;
    }

    /// <summary>A conv + BatchNorm + Linear stack covers all three groups in six parameters.</summary>
    private sealed class TinyNet : Module<Tensor, Tensor>
    {
        private readonly Conv2d _conv;
        private readonly BatchNorm2d _norm;
        private readonly Linear _head;

        public TinyNet()
            : base(nameof(TinyNet))
        {
            _conv = Conv2d(3, 4, kernel_size: 3, padding: 1);
            _norm = BatchNorm2d(4);
            _head = Linear(4, 2);
            register_module("conv", _conv);
            register_module("norm", _norm);
            register_module("head", _head);
        }

        public override Tensor forward(Tensor input) =>
            _head.forward(_norm.forward(_conv.forward(input)).mean([2, 3]));
    }
}
