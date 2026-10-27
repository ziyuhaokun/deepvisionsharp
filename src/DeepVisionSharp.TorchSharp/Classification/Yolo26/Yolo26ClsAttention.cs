using TorchSharp;
using TorchSharp.Modules;
using static TorchSharp.torch;
using static TorchSharp.torch.nn;

namespace DeepVisionSharp.TorchSharp.Classification.Yolo26;

/// <summary>
/// Position-sensitive self-attention. Q and K use half of the head dimension; a depthwise 3x3
/// convolution supplies the positional term.
/// </summary>
internal sealed class YoloAttention : Module<Tensor, Tensor>
{
    private readonly YoloConv _qkv;
    private readonly YoloConv _proj;
    private readonly YoloConv _pe;
    private readonly int _heads;
    private readonly long _keyDim;
    private readonly long _headDim;
    private readonly double _scale;

    public YoloAttention(long channels, int heads, double attentionRatio)
        : base(nameof(YoloAttention))
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(heads, 1);
        if (channels % heads != 0)
        {
            throw new ArgumentException($"通道数 {channels} 必须能被注意力头数 {heads} 整除。", nameof(channels));
        }

        _heads = heads;
        _headDim = channels / heads;
        _keyDim = (long)(_headDim * attentionRatio);
        _scale = Math.Pow(_keyDim, -0.5);
        var qkvChannels = channels + (2 * _keyDim * heads);
        _qkv = new YoloConv(channels, qkvChannels, kernel: 1, activate: false);
        _proj = new YoloConv(channels, channels, kernel: 1, activate: false);
        _pe = new YoloConv(channels, channels, kernel: 3, groups: channels, activate: false);
        register_module("qkv", _qkv);
        register_module("proj", _proj);
        register_module("pe", _pe);
    }

    public override Tensor forward(Tensor input)
    {
        using var scope = NewDisposeScope();
        var batch = input.shape[0];
        var channels = input.shape[1];
        var height = input.shape[2];
        var width = input.shape[3];
        var tokens = height * width;

        using var qkv = _qkv.forward(input).view(batch, _heads, (2 * _keyDim) + _headDim, tokens);
        var parts = qkv.split([_keyDim, _keyDim, _headDim], dim: 2);
        using var query = parts[0];
        using var key = parts[1];
        using var value = parts[2];

        using var scores = (query * _scale).transpose(-2, -1).matmul(key).softmax(-1);
        using var attended = value.matmul(scores.transpose(-2, -1)).view(batch, channels, height, width);
        using var positional = _pe.forward(value.reshape(batch, channels, height, width));
        return _proj.forward(attended + positional).MoveToOuterDisposeScope();
    }
}

/// <summary>Attention residual followed by a two-convolution feed-forward residual.</summary>
internal sealed class PsaBlock : Module<Tensor, Tensor>
{
    private readonly YoloAttention _attention;
    private readonly Sequential _feedForward;

    public PsaBlock(long channels, double attentionRatio, int heads)
        : base(nameof(PsaBlock))
    {
        _attention = new YoloAttention(channels, heads, attentionRatio);
        _feedForward = Sequential(
            ("0", (Module<Tensor, Tensor>)new YoloConv(channels, channels * 2, kernel: 1)),
            ("1", new YoloConv(channels * 2, channels, kernel: 1, activate: false)));
        register_module("attn", _attention);
        register_module("ffn", _feedForward);
    }

    public override Tensor forward(Tensor input)
    {
        using var scope = NewDisposeScope();
        var value = input + _attention.forward(input);
        value += _feedForward.forward(value);
        return value.MoveToOuterDisposeScope();
    }
}

/// <summary>Split channels in half, run PSA blocks on one half, then merge them.</summary>
internal sealed class C2Psa : Module<Tensor, Tensor>
{
    private readonly YoloConv _cv1;
    private readonly YoloConv _cv2;
    private readonly Sequential _blocks;
    private readonly long _hidden;

    public C2Psa(long inputChannels, long outputChannels, int repeats, double expansion = 0.5)
        : base(nameof(C2Psa))
    {
        if (inputChannels != outputChannels)
        {
            throw new ArgumentException("C2PSA 的输入通道和输出通道必须相同。", nameof(outputChannels));
        }

        _hidden = (long)(inputChannels * expansion);
        // Ultralytics uses hidden // 64 heads with no floor; below 64 channels it has no valid head count.
        if (_hidden < 64)
        {
            throw new ArgumentException($"C2PSA 隐藏通道数 {_hidden} 小于 64，无法确定注意力头数。", nameof(inputChannels));
        }

        var heads = (int)(_hidden / 64);
        _cv1 = new YoloConv(inputChannels, 2 * _hidden, kernel: 1);
        _blocks = Sequential(Enumerable.Range(0, repeats)
            .Select(index => (
                index.ToString(),
                (Module<Tensor, Tensor>)new PsaBlock(_hidden, attentionRatio: 0.5, heads)))
            .ToArray());
        _cv2 = new YoloConv(2 * _hidden, outputChannels, kernel: 1);
        register_module("cv1", _cv1);
        register_module("m", _blocks);
        register_module("cv2", _cv2);
    }

    public override Tensor forward(Tensor input)
    {
        using var scope = NewDisposeScope();
        var parts = _cv1.forward(input).split([_hidden, _hidden], dim: 1);
        var processed = _blocks.forward(parts[1]);
        return _cv2.forward(cat([parts[0], processed], dim: 1)).MoveToOuterDisposeScope();
    }
}

/// <summary>1x1 projection to 1280 channels, global pooling, dropout, then the class linear layer.</summary>
internal sealed class YoloClassify : Module<Tensor, Tensor>
{
    private readonly YoloConv _conv;
    private readonly AdaptiveAvgPool2d _pool;
    private readonly Dropout _drop;
    private readonly Linear _linear;

    public YoloClassify(long inputChannels, int numClasses)
        : base(nameof(YoloClassify))
    {
        _conv = new YoloConv(inputChannels, 1280, kernel: 1);
        _pool = AdaptiveAvgPool2d(1);
        _drop = Dropout(0);
        _linear = Linear(1280, numClasses);
        register_module("conv", _conv);
        register_module("pool", _pool);
        register_module("drop", _drop);
        register_module("linear", _linear);
    }

    public override Tensor forward(Tensor input)
    {
        using var scope = NewDisposeScope();
        var value = _pool.forward(_conv.forward(input)).flatten(1);
        value = _drop.forward(value);
        return _linear.forward(value).MoveToOuterDisposeScope();
    }
}
