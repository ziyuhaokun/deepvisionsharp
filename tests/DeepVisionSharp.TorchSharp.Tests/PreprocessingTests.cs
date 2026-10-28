using OpenCvSharp;
using TorchSharp;
using Xunit;
using DeepVisionSharp.TorchSharp.Models;
using DeepVisionSharp.TorchSharp.Tensors;

namespace DeepVisionSharp.TorchSharp.Tests;

/// <summary>
/// The two preprocessing pipelines a model can be trained and served under. Mixing them up silently
/// costs accuracy, so the numbers here are computed by hand from the published formula rather than
/// from the implementation.
/// </summary>
public sealed class PreprocessingTests
{
    private const float MeanR = 0.485f;
    private const float MeanG = 0.456f;
    private const float MeanB = 0.406f;
    private const float StdR = 0.229f;
    private const float StdG = 0.224f;
    private const float StdB = 0.225f;

    [Fact]
    public void UnitScaleV1_divides_by_255_and_nothing_else()
    {
        using var mat = SingleColorMat(0, 255, 128);
        var destination = new float[3];

        TorchSharpImageTensor.FillNormalizedData(mat, destination, PreprocessingVersion.UnitScaleV1);

        Assert.Equal(0f, destination[0], 6);
        Assert.Equal(1f, destination[1], 6);
        Assert.Equal(128f / 255f, destination[2], 6);
    }

    [Fact]
    public void ImageNetV2_normalises_each_channel_with_its_own_mean_and_std()
    {
        // Same value in every channel: v1 would give three identical outputs, so any difference
        // between them proves the per-channel statistics were applied.
        using var mat = SingleColorMat(100, 100, 100);
        var destination = new float[3];

        TorchSharpImageTensor.FillNormalizedData(mat, destination, PreprocessingVersion.ImageNetV2);

        Assert.NotEqual(destination[0], destination[1]);
        Assert.NotEqual(destination[1], destination[2]);
        Assert.NotEqual(destination[0], destination[2]);
    }

    [Fact]
    public void ImageNetV2_matches_the_formula_on_a_known_pixel()
    {
        // (value / 255 - mean) / std, per channel, in R/G/B order.
        using var mat = SingleColorMat(100, 150, 200);
        var destination = new float[3];

        TorchSharpImageTensor.FillNormalizedData(mat, destination, PreprocessingVersion.ImageNetV2);

        Assert.Equal((100f / 255f - MeanR) / StdR, destination[0], 5);
        Assert.Equal((150f / 255f - MeanG) / StdG, destination[1], 5);
        Assert.Equal((200f / 255f - MeanB) / StdB, destination[2], 5);
    }

    [Fact]
    public void ImageNetV2_produces_a_negative_value_below_the_mean()
    {
        // A dark pixel must end up below zero; the old pipeline could never do that.
        using var mat = SingleColorMat(20, 20, 20);
        var destination = new float[3];

        TorchSharpImageTensor.FillNormalizedData(mat, destination, PreprocessingVersion.ImageNetV2);

        Assert.All(destination, value => Assert.True(value < 0f, $"expected a negative value, got {value}"));
    }

    [Fact]
    public void The_two_pipelines_disagree_on_the_same_pixels()
    {
        using var mat = SingleColorMat(100, 150, 200);
        var v1 = new float[3];
        var v2 = new float[3];

        TorchSharpImageTensor.FillNormalizedData(mat, v1, PreprocessingVersion.UnitScaleV1);
        TorchSharpImageTensor.FillNormalizedData(mat, v2, PreprocessingVersion.ImageNetV2);

        for (var channel = 0; channel < 3; channel++)
        {
            Assert.NotEqual(v1[channel], v2[channel]);
        }
    }

    [Fact]
    public void ImageNetV2_is_ignored_for_single_channel_images()
    {
        // The version is a 3-channel pipeline; a grayscale tensor must stay plain 0..1.
        using var mat = new Mat(1, 1, MatType.CV_8UC1);
        mat.Set(0, 0, 128);
        var destination = new float[1];

        TorchSharpImageTensor.FillNormalizedData(mat, destination, PreprocessingVersion.ImageNetV2);

        Assert.Equal(128f / 255f, destination[0], 6);
    }

    [Fact]
    public void ToClassificationTensor_keeps_the_version_all_the_way_into_the_tensor()
    {
        using var mat = SingleColorMat(100, 150, 200);

        using var v1 = TorchSharpImageTensor.ToClassificationTensor(
            mat, 1, 1, torch.CPU, preprocessing: PreprocessingVersion.UnitScaleV1);
        using var v2 = TorchSharpImageTensor.ToClassificationTensor(
            mat, 1, 1, torch.CPU, preprocessing: PreprocessingVersion.ImageNetV2);

        Assert.Equal(v1.shape, v2.shape);
        var v1Data = v1.data<float>().ToArray();
        var v2Data = v2.data<float>().ToArray();
        for (var index = 0; index < v1Data.Length; index++)
        {
            Assert.NotEqual(v1Data[index], v2Data[index]);
        }
    }

    [Fact]
    public void ToClassificationTensor_puts_channels_in_rgb_order()
    {
        // A pure red source must light up the first channel only. OpenCV reads BGR, so this also
        // catches a channel swap, which is the other way the two pipelines can silently disagree.
        using var mat = SingleColorMat(200, 0, 0);

        using var tensor = TorchSharpImageTensor.ToClassificationTensor(
            mat, 1, 1, torch.CPU, preprocessing: PreprocessingVersion.UnitScaleV1);

        var data = tensor.data<float>().ToArray();
        Assert.All(data[..1], value => Assert.Equal(200f / 255f, value, 6));
        Assert.All(data[1..2], value => Assert.Equal(0f, value, 6));
        Assert.All(data[2..], value => Assert.Equal(0f, value, 6));
    }

    [Fact]
    public void FillNormalizedData_rejects_a_buffer_that_is_too_small()
    {
        // One pixel needs 3 floats; 2 cannot hold it, so the call must refuse rather than overflow.
        using var mat = SingleColorMat(100, 150, 200);

        Assert.Throws<ArgumentException>(() =>
        {
            var destination = new float[2];
            TorchSharpImageTensor.FillNormalizedData(mat, destination, PreprocessingVersion.UnitScaleV1);
        });
    }

    [Theory]
    [InlineData("unit_scale_v1", PreprocessingVersion.UnitScaleV1)]
    [InlineData("imagenet_v2", PreprocessingVersion.ImageNetV2)]
    [InlineData("  ImageNet_V2  ", PreprocessingVersion.ImageNetV2)]
    [InlineData("", PreprocessingVersion.UnitScaleV1)]
    [InlineData(null, PreprocessingVersion.UnitScaleV1)]
    public void PreprocessingVersions_parses_the_wire_names(string? wire, PreprocessingVersion expected)
    {
        Assert.Equal(expected, PreprocessingVersions.Parse(wire));
    }

    [Theory]
    [InlineData(PreprocessingVersion.UnitScaleV1, "unit_scale_v1")]
    [InlineData(PreprocessingVersion.ImageNetV2, "imagenet_v2")]
    public void PreprocessingVersions_round_trips(PreprocessingVersion version, string expected)
    {
        Assert.Equal(expected, version.ToWireValue());
        Assert.Equal(version, PreprocessingVersions.Parse(version.ToWireValue()));
    }

    /// <summary>A 2x2 BGR image whose every pixel is the given colour.</summary>
    private static Mat SingleColorMat(byte red, byte green, byte blue)
        => new(1, 1, MatType.CV_8UC3, new OpenCvSharp.Scalar(blue, green, red));
}
