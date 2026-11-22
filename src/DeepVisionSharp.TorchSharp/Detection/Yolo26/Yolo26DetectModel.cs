using TorchSharp;
using TorchSharp.Modules;
using static TorchSharp.torch;
using static TorchSharp.torch.nn;
using DeepVisionSharp.TorchSharp.Classification.Yolo26;

namespace DeepVisionSharp.TorchSharp.Detection.Yolo26;

/// <summary>
/// YOLO26 detection network: the classification backbone plus an SPPF, a three-scale head, and a
/// Detect head. <c>reg_max</c> is 1, so each box is four raw left/top/right/bottom distances and
/// there is no distribution focal loss.
/// </summary>
/// <remarks>
/// The forward returns raw predictions, one tensor per scale shaped (batch, 4 + classes, height,
/// width). Decoding and the top-k selection stay with the caller, which keeps the training loss on
/// the same tensors inference reads.
/// </remarks>
public sealed class Yolo26DetectModel : Module<Tensor, Tensor[]>
{
    private const int RegMax = 1;

    private readonly Module<Tensor, Tensor>[] _backbone;
    private readonly Sppf _sppf;
    private readonly C2Psa _psa;
    private readonly Upsample _up1;
    private readonly C3k2 _mergeP4;
    private readonly Upsample _up2;
    private readonly C3k2 _mergeP3;
    private readonly YoloConv _downToP4;
    private readonly C3k2 _outP4;
    private readonly YoloConv _downToP5;
    private readonly C3k2 _outP5;
    private readonly DetectHead _p3;
    private readonly DetectHead _p4;
    private readonly DetectHead _p5;

    private Yolo26DetectModel(string name, int inputChannels, int numClasses, Yolo26ClsScale scale)
        : base(name)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(numClasses, 1);
        NumClasses = numClasses;
        var channels = BuildChannels(scale);
        var repeats = Math.Max((int)Math.Round(2 * scale.Depth), 1);

        // yolo26.yaml reduces P3 from the backbone tap (512*w) to 256*w in the head. Using the tap
        // width here would make stage 16 and the Detect head two times too wide for the official
        // checkpoint; channels[2] is exactly 256*w for every scale.
        var headP3 = channels[2];

        // Indices match yolo26.yaml: 0..8 are the shared backbone, 4 and 6 are the P3 and P4 taps.
        _backbone =
        [
            new YoloConv(inputChannels, channels[0], kernel: 3, stride: 2),
            new YoloConv(channels[0], channels[1], kernel: 3, stride: 2),
            new C3k2(channels[1], channels[2], repeats, c3k: false, expansion: 0.25),
            new YoloConv(channels[2], channels[3], kernel: 3, stride: 2),
            new C3k2(channels[3], channels[4], repeats, c3k: false, expansion: 0.25),
            new YoloConv(channels[4], channels[5], kernel: 3, stride: 2),
            new C3k2(channels[5], channels[6], repeats, c3k: true),
            new YoloConv(channels[6], channels[7], kernel: 3, stride: 2),
            new C3k2(channels[7], channels[8], repeats, c3k: true)
        ];
        _sppf = new Sppf(channels[8], channels[8]);
        _psa = new C2Psa(channels[8], channels[8], repeats);

        _up1 = Upsample(scale_factor: [2.0, 2.0], mode: UpsampleMode.Nearest);
        _mergeP4 = new C3k2(channels[8] + channels[6], channels[6], repeats, c3k: true);
        _up2 = Upsample(scale_factor: [2.0, 2.0], mode: UpsampleMode.Nearest);
        // Stage 16 takes the P4 feature and the backbone's P3 tap (512*w, not the head's 256*w),
        // then narrows them to the head P3 width.
        _mergeP3 = new C3k2(channels[6] + channels[4], headP3, repeats, c3k: true);
        _downToP4 = new YoloConv(headP3, headP3, kernel: 3, stride: 2);
        _outP4 = new C3k2(headP3 + channels[6], channels[6], repeats, c3k: true);
        _downToP5 = new YoloConv(channels[6], channels[6], kernel: 3, stride: 2);
        // yolo26.yaml declares n=1 for the last C3k2; parse_model only scales repeats when n>1.
        _outP5 = new C3k2(channels[6] + channels[8], channels[8], repeats: 1, c3k: true, expansion: 0.5, attention: true);

        // Ultralytics Detect sizes every branch from the head's P3 width (layer 16 output).
        _p3 = new DetectHead(headP3, headP3, numClasses, BaseStride);
        _p4 = new DetectHead(channels[6], headP3, numClasses, BaseStride * 2);
        _p5 = new DetectHead(channels[8], headP3, numClasses, BaseStride * 4);

        Register("backbone", _backbone);
        register_module("sppf", _sppf);
        register_module("psa", _psa);
        register_module("merge_p4", _mergeP4);
        register_module("merge_p3", _mergeP3);
        register_module("down_p4", _downToP4);
        register_module("out_p4", _outP4);
        register_module("down_p5", _downToP5);
        register_module("out_p5", _outP5);
        register_module("head_p3", _p3);
        register_module("head_p4", _p4);
        register_module("head_p5", _p5);
    }

    public int NumClasses { get; }

    internal bool UseSppfShortcut
    {
        get => _sppf.UseShortcut;
        set => _sppf.UseShortcut = value;
    }

    /// <summary>Stride of the finest head (P3); P4/P5 are 2x/4x this.</summary>
    public const int BaseStride = 8;

    /// <summary>Creates the n-scale model; kept for the generated-dataset test.</summary>
    public static Yolo26DetectModel Create(int inputChannels, int numClasses) =>
        Create("yolo26n-det", inputChannels, numClasses);

    /// <summary>Creates a detection model from a product key such as <c>yolo26s-det</c>.</summary>
    public static Yolo26DetectModel Create(string archName, int inputChannels, int numClasses)
    {
        var scale = Yolo26ClsScale.Parse(archName);
        return new($"yolo26{scale.Name}-det", inputChannels, numClasses, scale);
    }

    public override Tensor[] forward(Tensor input)
    {
        using var scope = NewDisposeScope();
        var value = input;
        Tensor? p3Tap = null;
        Tensor? p4Tap = null;
        for (var index = 0; index < _backbone.Length; index++)
        {
            value = _backbone[index].forward(value);
            if (index == 4)
            {
                p3Tap = value;
            }
            else if (index == 6)
            {
                p4Tap = value;
            }
        }

        var p5In = _psa.forward(_sppf.forward(value));
        var mergedP4 = _mergeP4.forward(cat([_up1.forward(p5In), p4Tap!], dim: 1));
        var p3 = _mergeP3.forward(cat([_up2.forward(mergedP4), p3Tap!], dim: 1));
        var p4 = _outP4.forward(cat([_downToP4.forward(p3), mergedP4], dim: 1));
        var p5 = _outP5.forward(cat([_downToP5.forward(p4), p5In], dim: 1));

        return
        [
            _p3.forward(p3).MoveToOuterDisposeScope(),
            _p4.forward(p4).MoveToOuterDisposeScope(),
            _p5.forward(p5).MoveToOuterDisposeScope()
        ];
    }

    private static int[] BuildChannels(Yolo26ClsScale scale)
    {
        int[] requested = [64, 128, 256, 256, 512, 512, 512, 1024, 1024];
        return requested
            .Select(item => (int)Math.Ceiling(Math.Min(item, scale.MaxChannels) * scale.Width / 8.0) * 8)
            .ToArray();
    }

    private void Register(string prefix, IReadOnlyList<Module<Tensor, Tensor>> modules)
    {
        for (var index = 0; index < modules.Count; index++)
        {
            register_module($"{prefix}_{index}", modules[index]);
        }
    }
}

/// <summary>Spatial pyramid pooling: three max pools at one kernel size, concatenated.</summary>
internal sealed class Sppf : Module<Tensor, Tensor>
{
    private readonly YoloConv _cv1;
    private readonly YoloConv _cv2;
    private readonly MaxPool2d _pool;
    internal bool UseShortcut { get; set; } = true;

    public Sppf(long inputChannels, long outputChannels)
        : base(nameof(Sppf))
    {
        var hidden = inputChannels / 2;
        _cv1 = new YoloConv(inputChannels, hidden, kernel: 1, activate: false);
        _cv2 = new YoloConv(hidden * 4, outputChannels, kernel: 1);
        _pool = MaxPool2d(kernel_size: [5, 5], stride: [1, 1], padding: [2, 2]);
        register_module("cv1", _cv1);
        register_module("cv2", _cv2);
    }

    public override Tensor forward(Tensor input)
    {
        using var scope = NewDisposeScope();
        var first = _cv1.forward(input);
        var second = _pool.forward(first);
        var third = _pool.forward(second);
        var fourth = _pool.forward(third);
        var pooled = _cv2.forward(cat([first, second, third, fourth], dim: 1));
        return (UseShortcut ? pooled + input : pooled).MoveToOuterDisposeScope();
    }
}

/// <summary>One scale of the detect head: a box branch and a class branch, both ending in a bare 1x1.</summary>
/// <remarks>
/// Branch widths follow Ultralytics <c>Detect</c>: <c>c2 = max(16, P3 / 4)</c> and
/// <c>c3 = max(P3, min(classes, 100))</c>, computed from the P3 channels for every scale. The final
/// biases use the Ultralytics prior (box 1.0, class <c>log(5 / nc / (640 / stride)^2)</c>) so a fresh
/// head starts out predicting almost no objects instead of a 50% score on every cell.
/// </remarks>
internal sealed class DetectHead : Module<Tensor, Tensor>
{
    private const double PriorImageSize = 640;

    private readonly Sequential _box;
    private readonly Sequential _cls;

    public DetectHead(long channels, long p3Channels, int numClasses, int stride)
        : base(nameof(DetectHead))
    {
        var boxChannels = Math.Max(16, p3Channels / 4);
        var classChannels = Math.Max(p3Channels, Math.Min(numClasses, 100));
        var boxOut = Conv2d(boxChannels, 4, 1);
        var classOut = Conv2d(classChannels, numClasses, 1);
        using (no_grad())
        {
            boxOut.bias!.fill_(1.0);
            classOut.bias!.fill_(Math.Log(5.0 / numClasses / Math.Pow(PriorImageSize / stride, 2)));
        }

        _box = Sequential(
            ("0", (Module<Tensor, Tensor>)new YoloConv(channels, boxChannels, kernel: 3)),
            ("1", new YoloConv(boxChannels, boxChannels, kernel: 3)),
            ("2", boxOut));
        _cls = Sequential(
            ("0", (Module<Tensor, Tensor>)new YoloConv(channels, channels, kernel: 3, groups: channels)),
            ("1", new YoloConv(channels, classChannels, kernel: 1)),
            ("2", new YoloConv(classChannels, classChannels, kernel: 3, groups: classChannels)),
            ("3", new YoloConv(classChannels, classChannels, kernel: 1)),
            ("4", classOut));
        register_module("box", _box);
        register_module("cls", _cls);
    }

    public override Tensor forward(Tensor input)
    {
        using var scope = NewDisposeScope();
        return cat([_box.forward(input), _cls.forward(input)], dim: 1).MoveToOuterDisposeScope();
    }
}
