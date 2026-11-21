using OpenCvSharp;
using CvSize = OpenCvSharp.Size;
using TorchSharp;
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

/// <summary>
/// Loads the Ultralytics YOLO26 bundles written by <c>scripts/fetch_pretrained.ps1</c> and checks them
/// against the hand-written models.
/// </summary>
/// <remarks>
/// The bundles are not part of the repository (11 MB each), so every test reports and returns instead
/// of failing when its bundle is missing; only a machine that ran the fetch script exercises them.
/// The sample image is <c>temp/weights/bus.jpg</c> (Ultralytics' own sample, a bus with people in it).
/// </remarks>
public sealed class Yolo26PretrainedInitializerTests
{
    private const string DetectionArch = "yolo26n";
    private const string ClassificationArch = "yolo26n-cls";
    private const string SampleImage = "temp/weights/zidane.jpg";
    private const int CocoPerson = 0;
    private const int CocoBus = 5;

    private readonly ITestOutputHelper _output;

    public Yolo26PretrainedInitializerTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Classification_bundle_fills_every_classifier_tensor()
    {
        var directory = Resolve(ClassificationArch);
        if (directory is null)
        {
            return;
        }

        using var bundle = PretrainedWeightsBundle.Load(directory);
        using var model = Yolo26ClsClassifier.Create("n", inputChannels: 3, numClasses: 1000);

        var result = Yolo26PretrainedInitializer.ApplyToClassifier(model, bundle);

        Report("yolo26n-cls -> Yolo26ClsClassifier(1000)", result);
        var expected = bundle.Tensors.Keys.Count(key => !IsBookkeeping(key));
        Assert.Equal(expected, result.Loaded);
        Assert.Empty(result.MissingKeys);
        Assert.Empty(result.UnexpectedKeys);
    }

    [Fact]
    public void Detection_bundle_covers_the_backbone_and_accounts_for_every_other_tensor()
    {
        var directory = Resolve(DetectionArch);
        if (directory is null)
        {
            return;
        }

        using var bundle = PretrainedWeightsBundle.Load(directory);
        using var model = Yolo26DetectModel.Create("yolo26n-det", inputChannels: 3, numClasses: 80);

        var result = Yolo26PretrainedInitializer.ApplyToDetection(model, bundle);

        Report("yolo26n -> Yolo26DetectModel(80)", result);
        AssertBackboneLoaded(result);

        // Every weight is either applied or named in the report; nothing may disappear silently.
        var accounted = bundle.Tensors.Keys.Count(key => !IsBookkeeping(key) && !IsUnusedTwin(key));
        Assert.Equal(accounted, result.Loaded + result.UnexpectedKeys.Count);

        var skipped = result.UnexpectedKeys
            .Where(key => bundle.Tensors.TryGetValue(key, out _))
            .Select(key => $"{key} {Shape(bundle.Tensors[key])}")
            .ToArray();
        _output.WriteLine($"skipped by shape ({skipped.Length}):");
        foreach (var line in skipped)
        {
            _output.WriteLine("  " + line);
        }


    }

    [Fact]
    public void Classification_bundle_feeds_a_detection_model_backbone()
    {
        var directory = Resolve(ClassificationArch);
        if (directory is null)
        {
            return;
        }

        using var bundle = PretrainedWeightsBundle.Load(directory);
        using var model = Yolo26DetectModel.Create("yolo26n-det", inputChannels: 3, numClasses: 80);

        var result = Yolo26PretrainedInitializer.ApplyToDetection(model, bundle);

        Report("yolo26n-cls -> Yolo26DetectModel(80)", result);
        AssertBackboneLoaded(result);
        // yolo26-cls.yaml puts the C2PSA at model.9; it is the detector's model.10.
        Assert.DoesNotContain(result.UnexpectedKeys, key => key.StartsWith("model.9.", StringComparison.Ordinal));
        Assert.DoesNotContain(result.MissingKeys, key => key.StartsWith("psa.", StringComparison.Ordinal));
    }

    [Fact]
    public void Classification_bundle_ranks_the_sample_image()
    {
        var directory = Resolve(ClassificationArch);
        var image = FindSampleImage();
        if (directory is null || image is null)
        {
            _output.WriteLine("skip: 需要分类 bundle 与 temp/weights/bus.jpg");
            return;
        }

        using var bundle = PretrainedWeightsBundle.Load(directory);
        using var model = Yolo26ClsClassifier.Create("n", inputChannels: 3, numClasses: 1000);
        Yolo26PretrainedInitializer.ApplyToClassifier(model, bundle);
        model.eval();

        using var source = TorchSharpImageTensor.LoadMat(image);
        using var prepared = ResizeShortestAndCenterCrop(source, 224);
        // The checkpoint's own transforms are Resize(224) + CenterCrop(224) + ToTensor +
        // Normalize(mean 0, std 1): Ultralytics classifies on /255 only, so this is the unit scale,
        // not the ImageNet mean/std the ResNet weights need.
        using var input = TorchSharpImageTensor.ToClassificationTensor(
            prepared, 224, 224, torch.CPU, preprocessing: PreprocessingVersion.UnitScaleV1);
        using var logits = model.forward(input);
        using var probabilities = logits.softmax(dim: -1);
        var (values, indices) = probabilities.topk(5, dim: -1);

        _output.WriteLine("top5:");
        for (var rank = 0; rank < 5; rank++)
        {
            _output.WriteLine($"  {rank + 1}. class {indices[0, rank].item<long>()} p={values[0, rank].item<float>():F4}");
        }

        Assert.True(values[0, 0].item<float>() > 0.1f, "top1 概率过低，分类权重可能没真正加载");
    }

    [Fact]
    public void Detection_bundle_finds_the_bus_and_people_in_the_sample_image()
    {
        var directory = Resolve(DetectionArch);
        var image = FindSampleImage();
        if (directory is null || image is null)
        {
            _output.WriteLine("skip: 需要检测 bundle 与 temp/weights/bus.jpg");
            return;
        }

        using var bundle = PretrainedWeightsBundle.Load(directory);
        using var model = Yolo26DetectModel.Create("yolo26n-det", inputChannels: 3, numClasses: 80);
        var stateBefore = model.state_dict();
        var before = stateBefore["head_p3.box.2.bias"].clone();
        var result = Environment.GetEnvironmentVariable("DV_TEMP_NOLOAD") == "1"
            ? null
            : Yolo26PretrainedInitializer.ApplyToDetection(model, bundle);
        if (result is not null) { Report("yolo26n -> detection inference", result); }

        var after = model.state_dict()["head_p3.box.2.bias"];
        _output.WriteLine("TEMP bias changed: " + before.sub(after).abs().max().item<float>() + " now=" + string.Join(",", after.data<float>().ToArray().Take(4)));
        using var expectedBias = bundle.Tensors["model.23.cv2.0.2.bias"];
        _output.WriteLine("TEMP bias expected: " + string.Join(",", expectedBias.data<float>().ToArray().Take(4)));
        model.eval();

        using var source = TorchSharpImageTensor.LoadMat(image);
        var letterbox = DetectionLetterbox.For(source.Width, source.Height, 640, 640);
        using var canvas = letterbox.Apply(source);
        var input = DetectionLetterbox.ToTensor(canvas, torch.CPU, PreprocessingVersion.LetterboxV1);

        _output.WriteLine("TEMP input stats: " + input.min().item<float>() + " " + input.max().item<float>() + " " + input.mean().item<float>() + " shape " + string.Join(",", input.shape));
        _output.WriteLine("TEMP canvas size: " + canvas.Width + "x" + canvas.Height + " source " + source.Width + "x" + source.Height + " fit " + letterbox.Fit.DestRect.X + "," + letterbox.Fit.DestRect.Y + " " + letterbox.Fit.DestRect.Width + "x" + letterbox.Fit.DestRect.Height);
        if (Environment.GetEnvironmentVariable("DV_TEMP_GRAY") == "1")
        {
            input.Dispose();
            input = zeros([1, 3, 640, 640]);
        }
        var predictions = model.forward(input);
        for (var pi = 0; pi < predictions.Length; pi++)
        {
            _output.WriteLine($"TEMP pred{pi} shape {string.Join(",", predictions[pi].shape)} max {predictions[pi].max().item<float>()} min {predictions[pi].min().item<float>()}");
        }
        foreach (var pred in predictions)
        {
            var logits = pred[0, 4..];
            var (vals, ids) = logits.reshape(80, -1).max(dim: 1);
            var bestScale = vals.argmax().item<long>();
            var cell = vals.argmax().item<long>();
            _output.WriteLine("TEMP scale max cell " + cell + " class " + bestScale + " logit " + vals[bestScale].item<float>());
            var where = ids[bestScale].item<long>();
            var y = where / pred.shape[3]; var x = where % pred.shape[3];
            _output.WriteLine("TEMP cell y,x=" + y + "," + x);
            var full = pred[0, (0..84), (int)y, (int)x].data<float>().ToArray();
            _output.WriteLine("TEMP channels 0..9 " + string.Join(",", full.Take(10).Select(v => v.ToString("F2"))));
            var tops = full.Select((v, i) => (v, i)).OrderByDescending(t => t.v).Take(8);
            _output.WriteLine("TEMP top channels " + string.Join(",", tops.Select(t => t.i + ":" + t.v.ToString("F2"))));
        }
        IReadOnlyList<ScoredDetection> detections;
        try
        {
            detections = DetectionTensorDecoder.DecodeAll(
                predictions,
                Yolo26DetectModel.BaseStride,
                scoreThreshold: 0.02f,
                iouThreshold: 0.45f,
                maxDetections: 300);
        }
        finally
        {
            foreach (var prediction in predictions)
            {
                prediction.Dispose();
            }
        }

        _output.WriteLine($"detections: {detections.Count}");
        foreach (var detection in detections.OrderByDescending(item => item.Score).Take(25))
        {
            _output.WriteLine(
                $"  class {detection.ClassIndex} score {detection.Score:F3} " +
                $"box {detection.X:F0},{detection.Y:F0} {detection.Width:F0}x{detection.Height:F0}");
        }

        Assert.Contains(detections, item => item.ClassIndex == CocoPerson && item.Score > 0.5f);
        Assert.Contains(detections, item => item.ClassIndex == CocoBus && item.Score > 0.5f);
    }

    private void AssertBackboneLoaded(PretrainedLoadResult result)
    {
        Assert.DoesNotContain(
            result.UnexpectedKeys,
            key => key.StartsWith("model.", StringComparison.Ordinal) && IsBackbone(key));
        Assert.DoesNotContain(
            result.MissingKeys,
            key => key.StartsWith("backbone_", StringComparison.Ordinal));
    }

    /// <summary>Graph indices 0..8 are the shared backbone in both checkpoints.</summary>
    private static bool IsBackbone(string key)
    {
        var rest = key["model.".Length..];
        var dot = rest.IndexOf('.', StringComparison.Ordinal);
        return dot > 0 && int.TryParse(rest[..dot], out var index) && index <= 8;
    }

    /// <summary>The NMS-free head twins are intentionally unused, so they are not "skipped".</summary>
    private static bool IsUnusedTwin(string key) =>
        key.StartsWith("model.23.one2one_", StringComparison.Ordinal);

    private static bool IsBookkeeping(string key) =>
        key.EndsWith(".num_batches_tracked", StringComparison.Ordinal);

    private static string Shape(Tensor tensor) => $"[{string.Join(", ", tensor.shape)}]";

    private void Report(string title, PretrainedLoadResult result)
    {
        _output.WriteLine($"{title}: loaded={result.Loaded} missing={result.Missing} unexpected={result.Unexpected}");
        foreach (var key in result.MissingKeys.Take(40))
        {
            _output.WriteLine("  missing " + key);
        }

        foreach (var key in result.UnexpectedKeys.Take(40))
        {
            _output.WriteLine("  unexpected " + key);
        }
    }

    /// <summary>The runner starts in its output folder, so look for the sample next to the sources.</summary>
    private static string? FindSampleImage()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            var candidate = Path.Combine(folder.FullName, SampleImage);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private string? Resolve(string archName)
    {
        var directory = PretrainedWeightsLocator.Resolve(archName);
        if (directory is null)
        {
            _output.WriteLine($"skip: 没有 {archName} 的 bundle（先跑 scripts/fetch_pretrained.ps1 -Arch {archName}）");
        }
        else
        {
            _output.WriteLine($"{archName} bundle: {directory}");
        }

        return directory;
    }

    /// <summary>
    /// torchvision's <c>Resize(224)</c> scales the shorter side and <c>CenterCrop(224)</c> then takes
    /// the middle square, which is what the checkpoint's transforms describe.
    /// </summary>
    private static Mat ResizeShortestAndCenterCrop(Mat source, int size)
    {
        var scale = size / (double)Math.Min(source.Width, source.Height);
        var width = Math.Max(size, (int)Math.Round(source.Width * scale));
        var height = Math.Max(size, (int)Math.Round(source.Height * scale));
        using var resized = new Mat();
        Cv2.Resize(source, resized, new CvSize(width, height), 0, 0, InterpolationFlags.Linear);
        var left = (width - size) / 2;
        var top = (height - size) / 2;
        using var window = new Mat(resized, new Rect(left, top, size, size));
        return window.Clone();
    }
}
