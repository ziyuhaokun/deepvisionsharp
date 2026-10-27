using TorchSharp;
using TorchSharp.Modules;
using static TorchSharp.torch;
using static TorchSharp.torch.nn;

namespace DeepVisionSharp.TorchSharp.Classification.Yolo26;

/// <summary>
/// YOLO26 classification network, built from the Ultralytics <c>yolo26-cls.yaml</c> graph.
/// </summary>
/// <remarks>
/// Module names follow <c>model.0</c> through <c>model.10</c> so an Ultralytics state dict can load
/// without key rewriting. The head returns logits in both train and eval mode; softmax stays with
/// the caller. BatchNorm uses the Ultralytics defaults, eps 1e-3 and momentum 0.03.
/// </remarks>
public sealed class Yolo26ClsClassifier : Module<Tensor, Tensor>
{
    private readonly Module<Tensor, Tensor>[] _stages;
    private readonly string _archName;
    private readonly int _numClasses;

    private Yolo26ClsClassifier(
        string archName,
        int inputChannels,
        int numClasses,
        Yolo26ClsScale scale)
        : base(nameof(Yolo26ClsClassifier))
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archName);
        ArgumentOutOfRangeException.ThrowIfLessThan(inputChannels, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(numClasses, 2);

        _archName = archName;
        _numClasses = numClasses;

        // parse_model forces c3k for m/l/x. n/s keep Bottleneck for the first two C3k2 stages
        // and use C3k for the two deeper stages, exactly as the YAML declares.
        var channels = BuildChannels(scale);
        var repeats = Repeat(2, scale.Depth);
        var c3k = scale.C3kStages;
        _stages =
        [
            new YoloConv(inputChannels, channels[0], kernel: 3, stride: 2),
            new YoloConv(channels[0], channels[1], kernel: 3, stride: 2),
            new C3k2(channels[1], channels[2], repeats, c3k.Stage2, expansion: 0.25),
            new YoloConv(channels[2], channels[3], kernel: 3, stride: 2),
            new C3k2(channels[3], channels[4], repeats, c3k.Stage4, expansion: 0.25),
            new YoloConv(channels[4], channels[5], kernel: 3, stride: 2),
            new C3k2(channels[5], channels[6], repeats, c3k.Stage6),
            new YoloConv(channels[6], channels[7], kernel: 3, stride: 2),
            new C3k2(channels[7], channels[8], repeats, c3k.Stage8),
            new C2Psa(channels[8], channels[8], repeats),
            new YoloClassify(channels[8], numClasses)
        ];

        var graph = Sequential(_stages.Select((stage, index) => (index.ToString(), stage)).ToArray());
        register_module("model", graph);
    }

    public string ArchName => _archName;

    public int NumClasses => _numClasses;

    public override Tensor forward(Tensor input)
    {
        using var scope = NewDisposeScope();
        var value = input;
        foreach (var stage in _stages)
        {
            value = stage.forward(value);
        }

        return value.MoveToOuterDisposeScope();
    }

    public static Yolo26ClsClassifier Create(string scale, int inputChannels, int numClasses)
    {
        var resolved = Yolo26ClsScale.Parse(scale);
        return new Yolo26ClsClassifier($"yolo26{resolved.Name}-cls", inputChannels, numClasses, resolved);
    }

    /// <summary>Official ImageNet classification parameter counts, including the 1000-way head.</summary>
    public static long OfficialParameterCount(string scale) => Yolo26ClsScale.Parse(scale).ParameterCount;

    private static int[] BuildChannels(Yolo26ClsScale scale)
    {
        // Widths from yolo26-cls.yaml for the two stem convs, the four C3k2 stages, and the
        // Conv between them. Stage 6 requests 512; the stride-2 Conv at index 7 is the first 1024 stage.
        int[] requested = [64, 128, 256, 256, 512, 512, 512, 1024, 1024];
        return requested
            .Select(value => MakeDivisible(Math.Min(value, scale.MaxChannels) * scale.Width, 8))
            .ToArray();
    }

    private static int Repeat(int count, double depth) =>
        count > 1 ? Math.Max((int)Math.Round(count * depth), 1) : count;

    private static int MakeDivisible(double value, int divisor) =>
        (int)Math.Ceiling(value / divisor) * divisor;
}

/// <summary>Which C3k2 stages use C3k inner blocks instead of Bottleneck, in model order.</summary>
internal readonly record struct C3kStagePattern(
    bool Stage2,
    bool Stage4,
    bool Stage6,
    bool Stage8);

internal readonly record struct Yolo26ClsScale(
    string Name,
    double Depth,
    double Width,
    int MaxChannels,
    C3kStagePattern C3kStages,
    long ParameterCount)
{
    public static Yolo26ClsScale Parse(string scale)
    {
        var normalized = scale.Trim().ToLowerInvariant();
        if (normalized.StartsWith("yolo26", StringComparison.Ordinal))
        {
            normalized = normalized["yolo26".Length..];
        }

        if (normalized.EndsWith("-cls", StringComparison.Ordinal))
        {
            normalized = normalized[..^"-cls".Length];
        }

        return normalized switch
        {
            "n" => new("n", 0.50, 0.25, 1024, new(false, false, true, true), 2_812_104),
            "s" => new("s", 0.50, 0.50, 1024, new(false, false, true, true), 6_724_008),
            "m" => new("m", 0.50, 1.00, 512, new(true, true, true, true), 11_634_216),
            "l" => new("l", 1.00, 1.00, 512, new(true, true, true, true), 14_115_624),
            "x" => new("x", 1.00, 1.50, 512, new(true, true, true, true), 29_637_064),
            _ => throw new ArgumentOutOfRangeException(nameof(scale), scale, "YOLO26-cls 规格只能是 n、s、m、l、x。")
        };
    }
}

/// <summary>Ultralytics Conv: Conv2d without bias, BatchNorm, then SiLU.</summary>
internal sealed class YoloConv : Module<Tensor, Tensor>
{
    private readonly Conv2d _conv;
    private readonly BatchNorm2d _bn;
    private readonly SiLU? _act;

    public YoloConv(
        long inputChannels,
        long outputChannels,
        long kernel,
        long stride = 1,
        long groups = 1,
        bool activate = true)
        : base(nameof(YoloConv))
    {
        var padding = kernel / 2;
        _conv = Conv2d(
            inputChannels,
            outputChannels,
            kernel,
            stride,
            padding,
            groups: groups,
            bias: false);
        _bn = BatchNorm2d(outputChannels, eps: 1e-3, momentum: 0.03);
        register_module("conv", _conv);
        register_module("bn", _bn);
        if (activate)
        {
            _act = SiLU();
            register_module("act", _act);
        }
    }

    public override Tensor forward(Tensor input)
    {
        using var scope = NewDisposeScope();
        var value = _bn.forward(_conv.forward(input));
        if (_act is not null)
        {
            value = _act.forward(value);
        }

        return value.MoveToOuterDisposeScope();
    }
}

/// <summary>Two-convolution bottleneck. The shortcut exists only when the channel counts match.</summary>
internal sealed class YoloBottleneck : Module<Tensor, Tensor>
{
    private readonly YoloConv _cv1;
    private readonly YoloConv _cv2;
    private readonly bool _add;

    public YoloBottleneck(
        long inputChannels,
        long outputChannels,
        bool shortcut,
        double expansion,
        long kernel1 = 3,
        long kernel2 = 3)
        : base(nameof(YoloBottleneck))
    {
        var hidden = (long)(outputChannels * expansion);
        _cv1 = new YoloConv(inputChannels, hidden, kernel1);
        _cv2 = new YoloConv(hidden, outputChannels, kernel2);
        _add = shortcut && inputChannels == outputChannels;
        register_module("cv1", _cv1);
        register_module("cv2", _cv2);
    }

    public override Tensor forward(Tensor input)
    {
        using var scope = NewDisposeScope();
        var value = _cv2.forward(_cv1.forward(input));
        if (_add)
        {
            value += input;
        }

        return value.MoveToOuterDisposeScope();
    }
}

/// <summary>C2f whose inner blocks are either a bottleneck or a two-bottleneck C3k.</summary>
internal sealed class C3k2 : Module<Tensor, Tensor>
{
    private readonly YoloConv _cv1;
    private readonly YoloConv _cv2;
    private readonly Module<Tensor, Tensor>[] _blocks;
    private readonly long _hidden;

    public C3k2(long inputChannels, long outputChannels, int repeats, bool c3k, double expansion = 0.5)
        : base(nameof(C3k2))
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(repeats, 1);
        _hidden = (long)(outputChannels * expansion);
        _cv1 = new YoloConv(inputChannels, 2 * _hidden, kernel: 1);
        // C3k2 replaces C2f's blocks with Bottleneck(self.c, self.c, shortcut, g), whose default expansion is 0.5.
        _blocks = Enumerable.Range(0, repeats)
            .Select(_ => c3k
                ? (Module<Tensor, Tensor>)new C3k(_hidden, _hidden)
                : new YoloBottleneck(_hidden, _hidden, shortcut: true, expansion: 0.5))
            .ToArray();
        _cv2 = new YoloConv((2 + repeats) * _hidden, outputChannels, kernel: 1);

        register_module("cv1", _cv1);
        register_module("m", ModuleList(_blocks.Cast<Module>().ToArray()));
        register_module("cv2", _cv2);
    }

    public override Tensor forward(Tensor input)
    {
        using var scope = NewDisposeScope();
        var parts = _cv1.forward(input).chunk(2, dim: 1);
        var values = new List<Tensor>(2 + _blocks.Length) { parts[0], parts[1] };
        foreach (var block in _blocks)
        {
            values.Add(block.forward(values[^1]));
        }

        return _cv2.forward(cat(values, dim: 1)).MoveToOuterDisposeScope();
    }
}

/// <summary>CSP block containing two bottlenecks on one branch.</summary>
internal sealed class C3k : Module<Tensor, Tensor>
{
    private readonly YoloConv _cv1;
    private readonly YoloConv _cv2;
    private readonly YoloConv _cv3;
    private readonly Sequential _blocks;

    public C3k(long inputChannels, long outputChannels, int repeats = 2, double expansion = 0.5)
        : base(nameof(C3k))
    {
        var hidden = (long)(outputChannels * expansion);
        _cv1 = new YoloConv(inputChannels, hidden, kernel: 1);
        _cv2 = new YoloConv(inputChannels, hidden, kernel: 1);
        _blocks = Sequential(Enumerable.Range(0, repeats)
            .Select(index => (
                index.ToString(),
                (Module<Tensor, Tensor>)new YoloBottleneck(hidden, hidden, shortcut: true, expansion: 1)))
            .ToArray());
        _cv3 = new YoloConv(2 * hidden, outputChannels, kernel: 1);
        register_module("cv1", _cv1);
        register_module("cv2", _cv2);
        register_module("m", _blocks);
        register_module("cv3", _cv3);
    }

    public override Tensor forward(Tensor input)
    {
        using var scope = NewDisposeScope();
        var processed = _blocks.forward(_cv1.forward(input));
        var skipped = _cv2.forward(input);
        return _cv3.forward(cat([processed, skipped], dim: 1)).MoveToOuterDisposeScope();
    }
}
