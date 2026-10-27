using TorchSharp;
using Xunit;
using DeepVisionSharp.TorchSharp.Classification;
using DeepVisionSharp.TorchSharp.Classification.ResNet;

namespace DeepVisionSharp.TorchSharp.Tests;

public sealed class TorchSharpClassifierTests
{
    [Theory]
    [InlineData("yolo26n", TorchSharpClassifierFactory.ArchYolo26NCls)]
    [InlineData("yolo26-n", TorchSharpClassifierFactory.ArchYolo26NCls)]
    [InlineData("yolo26n-cls", TorchSharpClassifierFactory.ArchYolo26NCls)]
    [InlineData("yolo26s", TorchSharpClassifierFactory.ArchYolo26SCls)]
    [InlineData("yolo26-s", TorchSharpClassifierFactory.ArchYolo26SCls)]
    [InlineData("yolo26m", TorchSharpClassifierFactory.ArchYolo26MCls)]
    [InlineData("yolo26l", TorchSharpClassifierFactory.ArchYolo26LCls)]
    [InlineData("yolo26x", TorchSharpClassifierFactory.ArchYolo26XCls)]
    [InlineData("resnet18", TorchSharpClassifierFactory.ArchResNet18)]
    [InlineData("resnet-18", TorchSharpClassifierFactory.ArchResNet18)]
    [InlineData("resnet18_v2", TorchSharpClassifierFactory.ArchResNet18)]
    [InlineData("resnet-tiny", TorchSharpClassifierFactory.ArchResNetTiny)]
    [InlineData("resnet34", TorchSharpClassifierFactory.ArchResNet34)]
    [InlineData("unknown_arch", TorchSharpClassifierFactory.ArchResNet18)]
    public void ResolveArch_canonicalizes_aliases_correctly(string input, string expected)
    {
        Assert.Equal(expected, TorchSharpClassifierFactory.ResolveArch(input));
    }

    [Fact]
    public void ResNet_zero_initializes_residual_bn2_weights()
    {
        using var model = TorchVisionResNetClassifier.CreateResNet18(inputChannels: 3, numClasses: 10);
        var state = model.state_dict();

        // All BasicBlock bn2 weights must be 0 (zero_init_residual)
        var bn2Weights = state.Where(kv => kv.Key.EndsWith(".bn2.weight", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(bn2Weights);
        foreach (var (key, weight) in bn2Weights)
        {
            var values = weight.data<float>().ToArray();
            Assert.True(values.All(v => v == 0f), $"Residual BN {key} should be all zeros.");
        }

        // Stem and first BN weights should be 1
        var bn1Weights = state.Where(kv => kv.Key.EndsWith(".bn1.weight", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(bn1Weights);
        foreach (var (key, weight) in bn1Weights)
        {
            var values = weight.data<float>().ToArray();
            Assert.True(values.All(v => v == 1f), $"BN {key} should be all ones.");
        }
    }

    [Fact]
    public void ImageTensor_ToClassificationTensor_matches_FillNormalizedData()
    {
        using var mat = new OpenCvSharp.Mat(16, 16, OpenCvSharp.MatType.CV_8UC3, new OpenCvSharp.Scalar(100, 150, 200));
        using var tensor = DeepVisionSharp.TorchSharp.Tensors.TorchSharpImageTensor.ToClassificationTensor(
            mat,
            targetWidth: 32,
            targetHeight: 32,
            targetDevice: torch.CPU,
            preprocessing: DeepVisionSharp.TorchSharp.Models.PreprocessingVersion.UnitScaleV1);

        Assert.Equal(new long[] { 1, 3, 32, 32 }, tensor.shape);
        var tensorData = tensor.data<float>().ToArray();

        // Also test FillNormalizedData into a rented buffer
        using var prepared = DeepVisionSharp.TorchSharp.Tensors.TorchSharpImageTensor.PrepareMat(mat, 32, 32);
        var buffer = new float[3 * 32 * 32];
        DeepVisionSharp.TorchSharp.Tensors.TorchSharpImageTensor.FillNormalizedData(
            prepared,
            buffer,
            DeepVisionSharp.TorchSharp.Models.PreprocessingVersion.UnitScaleV1);

        Assert.Equal(tensorData, buffer);
    }

    [Fact]
    public void BatchTensor_creation_matches_individual_stacking()
    {
        const int batchCount = 3;
        const int channels = 3;
        const int width = 16;
        const int height = 16;
        const int sampleElements = channels * width * height;
        const int totalElements = batchCount * sampleElements;

        var rentedBuffer = System.Buffers.ArrayPool<float>.Shared.Rent(totalElements);
        var individualTensors = new List<torch.Tensor>();

        try
        {
            for (var i = 0; i < batchCount; i++)
            {
                using var mat = new OpenCvSharp.Mat(16, 16, OpenCvSharp.MatType.CV_8UC3, new OpenCvSharp.Scalar(i * 30, i * 40, i * 50));
                using var prepared = DeepVisionSharp.TorchSharp.Tensors.TorchSharpImageTensor.PrepareMat(mat, width, height);

                // Fill batch buffer slice
                var slice = rentedBuffer.AsSpan(i * sampleElements, sampleElements);
                DeepVisionSharp.TorchSharp.Tensors.TorchSharpImageTensor.FillNormalizedData(
                    prepared,
                    slice,
                    DeepVisionSharp.TorchSharp.Models.PreprocessingVersion.UnitScaleV1);

                // Create individual tensor for comparison
                individualTensors.Add(DeepVisionSharp.TorchSharp.Tensors.TorchSharpImageTensor.ToClassificationTensor(
                    mat, width, height, torch.CPU, preprocessing: DeepVisionSharp.TorchSharp.Models.PreprocessingVersion.UnitScaleV1));
            }

            using var batchTensor = torch.tensor(
                rentedBuffer.AsMemory(0, totalElements),
                [batchCount, channels, height, width],
                dtype: torch.ScalarType.Float32,
                device: torch.CPU);

            using var stackedTensor = torch.cat(individualTensors.ToArray(), dim: 0);

            Assert.Equal(stackedTensor.shape, batchTensor.shape);
            var stackedData = stackedTensor.data<float>().ToArray();
            var batchData = batchTensor.data<float>().ToArray();
            Assert.Equal(stackedData, batchData);
        }
        finally
        {
            System.Buffers.ArrayPool<float>.Shared.Return(rentedBuffer);
            foreach (var t in individualTensors)
            {
                t.Dispose();
            }
        }
    }

    [Fact]
    public void ImageTensor_CropTile_slices_correct_region()
    {
        using var source = new OpenCvSharp.Mat(100, 100, OpenCvSharp.MatType.CV_8UC3, new OpenCvSharp.Scalar(10, 20, 30));
        var tileWindow = new DeepVisionSharp.Tiling.TileWindow(
            Index: 0,
            Source: new DeepVisionSharp.Tiling.PixelRect(10, 20, 30, 40),
            Canvas: new DeepVisionSharp.Tiling.PixelSize(30, 40),
            Dest: new DeepVisionSharp.Tiling.PixelRect(0, 0, 30, 40));

        using var cropped = DeepVisionSharp.TorchSharp.Tensors.TorchSharpImageTensor.CropTile(source, tileWindow);

        Assert.Equal(30, cropped.Width);
        Assert.Equal(40, cropped.Height);
    }
}
