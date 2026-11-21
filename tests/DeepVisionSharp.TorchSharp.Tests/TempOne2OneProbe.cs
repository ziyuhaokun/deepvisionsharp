using TorchSharp;
using Xunit;
using Xunit.Abstractions;
using static TorchSharp.torch;
using DeepVisionSharp.TorchSharp.Detection;
using DeepVisionSharp.TorchSharp.Detection.Yolo26;
using DeepVisionSharp.TorchSharp.Models;
using DeepVisionSharp.TorchSharp.Tensors;

namespace DeepVisionSharp.TorchSharp.Tests;

// TEMPORARY diagnostic: where does the detection head go wrong?
public sealed class TempOne2OneProbe
{
    private readonly ITestOutputHelper _output;

    public TempOne2OneProbe(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Probe_head_scales()
    {
        var image = FindSample("temp/weights/bus.jpg");
        var directory = PretrainedWeightsLocator.Resolve("yolo26n");
        if (image is null || directory is null)
        {
            _output.WriteLine("skip: missing sample or bundle");
            return;
        }

        using var bundle = Classification.Pretrained.PretrainedWeightsBundle.Load(directory);
        using var model = Yolo26DetectModel.Create("yolo26n-det", inputChannels: 3, numClasses: 80);
        _output.WriteLine("loaded=" + Classification.Pretrained.Yolo26PretrainedInitializer.ApplyToDetection(model, bundle).Loaded);
        model.eval();

        using var raw = TorchSharpImageTensor.LoadMat(image);
        _output.WriteLine($"source {raw.Width}x{raw.Height} channels={raw.Channels()}");
        var letterbox = DetectionLetterbox.For(raw.Width, raw.Height, 640, 640);
        _output.WriteLine($"fit dest={letterbox.Fit.DestRect.X},{letterbox.Fit.DestRect.Y} {letterbox.Fit.DestRect.Width}x{letterbox.Fit.DestRect.Height} scale={letterbox.Fit.ScaleX}");
        using var canvas = letterbox.Apply(raw);
        using var input = DetectionLetterbox.ToTensor(canvas, torch.CPU, PreprocessingVersion.LetterboxV1);
        _output.WriteLine($"input min={input.min().item<float>():F3} max={input.max().item<float>():F3} mean={input.mean().item<float>():F3} shape={string.Join(",", input.shape)}");

        var predictions = model.forward(input);
        try
        {
            for (var index = 0; index < predictions.Length; index++)
            {
                var prediction = predictions[index];
                var h = (int)prediction.shape[2];
                var w = (int)prediction.shape[3];
                _output.WriteLine($"scale{index} stride={Yolo26DetectModel.BaseStride << index} shape={string.Join(",", prediction.shape)} " +
                                  $"boxAbsMax={prediction[0, 0..4].abs().max().item<float>():F2}");

                var classes = prediction[0, 4..].reshape(80, -1);
                var (values, indices) = classes.max(dim: 1);
                var (topValues, topClasses) = values.topk(5);
                for (var rank = 0; rank < 5; rank++)
                {
                    var classIndex = topClasses[rank].item<long>();
                    var cell = indices[classIndex].item<long>();
                    var logit = topValues[rank].item<float>();
                    _output.WriteLine($"   top{rank + 1} class={classIndex} logit={logit:F3} sigmoid={1.0 / (1.0 + Math.Exp(-logit)):F3} cell=({cell / w},{cell % w})");
                }
            }
        }
        finally
        {
            foreach (var prediction in predictions)
            {
                prediction.Dispose();
            }
        }
    }

    private static string? FindSample(string relative)
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            var candidate = Path.Combine(folder.FullName, relative);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
