using TorchSharp;
using Xunit;
using DeepVisionSharp.TorchSharp.Classification;
using DeepVisionSharp.TorchSharp.Classification.Yolo26;

namespace DeepVisionSharp.TorchSharp.Tests;

public sealed class Yolo26ClsClassifierTests
{
    [Theory]
    [InlineData("n", 2_812_104L)]
    [InlineData("s", 6_724_008L)]
    [InlineData("m", 11_634_216L)]
    [InlineData("l", 14_115_624L)]
    [InlineData("x", 29_637_064L)]
    public void Scale_has_the_ultralytics_parameter_count(string scale, long expected)
    {
        using var model = Yolo26ClsClassifier.Create(scale, inputChannels: 3, numClasses: 1000);

        var parameters = model.parameters().Sum(parameter => parameter.numel());

        // The expected counts are the published ultralytics numbers, independent of our own table;
        // comparing against OfficialParameterCount as well would only re-read the same source.
        Assert.Equal(expected, parameters);
    }

    // Pins the official yolo26-cls module numbering and widths. These are deliberate structure locks,
    // not incidental internals: renaming a submodule or reordering stages changes the layout this
    // test guards, and the weights-loading path depends on it.
    [Theory]
    [InlineData("n", 128, 256, false, false, true, true)]
    [InlineData("s", 256, 512, false, false, true, true)]
    [InlineData("m", 512, 512, true, true, true, true)]
    [InlineData("l", 512, 512, true, true, true, true)]
    [InlineData("x", 768, 768, true, true, true, true)]
    public void Scale_uses_the_ultralytics_stage_layout(
        string scale,
        int stage6Channels,
        int stage8Channels,
        bool stage2UsesC3k,
        bool stage4UsesC3k,
        bool stage6UsesC3k,
        bool stage8UsesC3k)
    {
        using var model = Yolo26ClsClassifier.Create(scale, inputChannels: 3, numClasses: 1000);
        var state = model.state_dict();

        // Stage 6 feeds stage 8, so both sides of the stride-2 conv must agree.
        Assert.Equal(stage6Channels, (int)state["model.6.cv2.conv.weight"].shape[0]);
        Assert.Equal(stage6Channels, (int)state["model.7.conv.weight"].shape[1]);
        Assert.Equal(stage8Channels, (int)state["model.8.cv2.conv.weight"].shape[0]);

        AssertC3kStage(state, 2, stage2UsesC3k);
        AssertC3kStage(state, 4, stage4UsesC3k);
        AssertC3kStage(state, 6, stage6UsesC3k);
        AssertC3kStage(state, 8, stage8UsesC3k);
    }

    [Fact]
    public void Forward_returns_logits_for_every_image_in_the_batch()
    {
        using var model = TorchSharpClassifierFactory.Create("yolo26n-cls", inputChannels: 3, numClasses: 4);
        model.eval();
        using var input = torch.randn(2, 3, 224, 224);

        using var logits = model.forward(input);

        Assert.Equal(new long[] { 2, 4 }, logits.shape);
        Assert.True(logits.isfinite().all().item<bool>());
    }

    [Theory]
    [InlineData("n")]
    [InlineData("s")]
    [InlineData("m")]
    [InlineData("l")]
    [InlineData("x")]
    public void Forward_works_for_every_scale(string scale)
    {
        using var model = Yolo26ClsClassifier.Create(scale, inputChannels: 3, numClasses: 3);
        model.eval();
        using var input = torch.randn(1, 3, 64, 64);

        using var logits = model.forward(input);

        Assert.Equal(new long[] { 1, 3 }, logits.shape);
        Assert.True(logits.isfinite().all().item<bool>());
    }

    [Fact]
    public void Unknown_architecture_stays_on_resnet18()
    {
        Assert.Equal(TorchSharpClassifierFactory.ArchResNet18, TorchSharpClassifierFactory.ResolveArch("yolo26z-cls"));
    }

    private static void AssertC3kStage(
        IReadOnlyDictionary<string, torch.Tensor> state,
        int stage,
        bool usesC3k)
    {
        // C3k adds cv3 and nests Bottlenecks; a Bottleneck stage has only cv1/cv2.
        Assert.Equal(usesC3k, state.ContainsKey($"model.{stage}.m.0.cv3.conv.weight"));
        if (usesC3k)
        {
            Assert.Contains($"model.{stage}.m.0.m.0.cv1.conv.weight", state.Keys);
        }
    }
}
