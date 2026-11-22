using OpenCvSharp;
using Xunit;
using Xunit.Abstractions;
using static TorchSharp.torch;
using DeepVisionSharp.TorchSharp.Classification.Pretrained;
using DeepVisionSharp.TorchSharp.Classification.Yolo26;
using DeepVisionSharp.TorchSharp.Detection;
using DeepVisionSharp.TorchSharp.Detection.Yolo26;
using DeepVisionSharp.TorchSharp.Models;
using DeepVisionSharp.TorchSharp.Tensors;

namespace DeepVisionSharp.TorchSharp.Tests;

[AttributeUsage(AttributeTargets.Method)]
public sealed class PretrainedFactAttribute : FactAttribute
{
    public PretrainedFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("DV_VERIFY_PRETRAINED") != "1")
        {
            Skip = "Set DV_VERIFY_PRETRAINED=1 with the official bundles and bus.jpg fixture.";
        }
    }
}

[Trait("Category", "Pretrained")]
public sealed class Yolo26PretrainedInitializerTests(ITestOutputHelper output)
{
    private const string DetectionArch = "yolo26n";
    private const string ClassificationArch = "yolo26n-cls";

    [PretrainedFact]
    public void Classification_bundle_fills_every_classifier_tensor()
    {
        using var bundle = OpenBundle(ClassificationArch);
        using var model = Yolo26ClsClassifier.Create("n", inputChannels: 3, numClasses: 1000);

        var result = Yolo26PretrainedInitializer.ApplyToClassifier(model, bundle);

        AssertComplete(result);
        Assert.Equal(bundle.Tensors.Keys.Count(key => !IsBookkeeping(key)), result.Loaded);
    }

    [PretrainedFact]
    public void Detection_bundle_fills_every_tensor_in_the_nms_model()
    {
        using var bundle = OpenBundle(DetectionArch);
        using var model = Yolo26DetectModel.Create("yolo26n-det", inputChannels: 3, numClasses: 80);

        var result = Yolo26PretrainedInitializer.ApplyToDetection(model, bundle);

        AssertComplete(result);
        Assert.Equal(bundle.Tensors.Keys.Count(key => !IsBookkeeping(key)
            && !key.StartsWith("model.23.one2one_", StringComparison.Ordinal)), result.Loaded);
    }

    [PretrainedFact]
    public void Classification_bundle_feeds_a_detection_model_backbone()
    {
        using var bundle = OpenBundle(ClassificationArch);
        using var model = Yolo26DetectModel.Create("yolo26n-det", inputChannels: 3, numClasses: 80);

        var result = Yolo26PretrainedInitializer.ApplyToDetection(model, bundle);

        Assert.True(result.Loaded > 0);
        Assert.DoesNotContain(result.UnexpectedKeys, key => key.StartsWith("model.9.", StringComparison.Ordinal));
        Assert.DoesNotContain(result.MissingKeys, key => key.StartsWith("backbone_", StringComparison.Ordinal)
            || key.StartsWith("psa.", StringComparison.Ordinal));
    }

    [PretrainedFact]
    public void Classification_bundle_ranks_the_sample_image()
    {
        using var scope = NewDisposeScope();
        using var noGrad = no_grad();
        using var bundle = OpenBundle(ClassificationArch);
        using var model = Yolo26ClsClassifier.Create("n", inputChannels: 3, numClasses: 1000);
        AssertComplete(Yolo26PretrainedInitializer.ApplyToClassifier(model, bundle));
        model.eval();

        using var source = TorchSharpImageTensor.LoadMat(FindBusImage());
        const int size = 224;
        var scale = size / (double)Math.Min(source.Width, source.Height);
        var width = Math.Max(size, (int)Math.Round(source.Width * scale));
        var height = Math.Max(size, (int)Math.Round(source.Height * scale));
        using var resized = new Mat();
        Cv2.Resize(source, resized, new OpenCvSharp.Size(width, height));
        using var crop = new Mat(resized, new Rect((width - size) / 2, (height - size) / 2, size, size));
        using var input = TorchSharpImageTensor.ToClassificationTensor(
            crop, size, size, CPU, preprocessing: PreprocessingVersion.UnitScaleV1);
        using var logits = model.forward(input);
        using var probabilities = logits.softmax(dim: -1);
        var score = probabilities.max().item<float>();

        output.WriteLine($"top1 confidence: {score:F3}");
        Assert.True(score > 0.1f);
    }

    [PretrainedFact]
    public void Detection_bundle_finds_the_bus_and_people_in_the_sample_image()
    {
        using var scope = NewDisposeScope();
        using var noGrad = no_grad();
        using var bundle = OpenBundle(DetectionArch);
        using var model = Yolo26DetectModel.Create("yolo26n-det", inputChannels: 3, numClasses: 80);
        AssertComplete(Yolo26PretrainedInitializer.ApplyToDetection(model, bundle));
        model.eval();

        using var source = TorchSharpImageTensor.LoadMat(FindBusImage());
        var letterbox = DetectionLetterbox.For(source.Width, source.Height, 640, 640);
        using var canvas = letterbox.Apply(source);
        using var input = DetectionLetterbox.ToTensor(canvas, CPU, PreprocessingVersion.LetterboxV1);
        var detections = DetectionTensorDecoder.DecodeAll(
            model.forward(input), Yolo26DetectModel.BaseStride, 0.25f, 0.45f, 100);

        foreach (var box in detections.Take(10))
        {
            output.WriteLine($"class {box.ClassIndex}: {box.Score:F3}");
        }

        Assert.Contains(detections, box => box.ClassIndex == 0 && box.Score > 0.5f);
        Assert.Contains(detections, box => box.ClassIndex == 5 && box.Score > 0.5f);
    }

    private static PretrainedWeightsBundle OpenBundle(string arch)
    {
        var directory = PretrainedWeightsLocator.Resolve(arch);
        Assert.True(directory is not null && Path.GetFileName(directory) == arch,
            $"Missing {arch} bundle. Run scripts/fetch_pretrained.ps1 -Arch {arch}.");
        return PretrainedWeightsBundle.Load(directory!);
    }

    private static bool IsBookkeeping(string key) =>
        key.EndsWith(".num_batches_tracked", StringComparison.Ordinal);

    private void AssertComplete(PretrainedLoadResult result)
    {
        output.WriteLine($"loaded={result.Loaded}, missing={result.Missing}, unexpected={result.Unexpected}");
        Assert.Empty(result.MissingKeys);
        Assert.Empty(result.UnexpectedKeys);
    }

    private static string FindBusImage()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            var candidate = Path.Combine(folder.FullName, "temp", "weights", "bus.jpg");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("Missing test image temp/weights/bus.jpg. See docs/development.md.");
    }
}
