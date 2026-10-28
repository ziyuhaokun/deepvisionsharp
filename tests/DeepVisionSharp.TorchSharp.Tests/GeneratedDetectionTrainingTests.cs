using TorchSharp;
using Xunit;
using DeepVisionSharp.TorchSharp.Detection.Yolo26;
using DeepVisionSharp.TorchSharp.Tensors;
using DvsDatasetGen;

namespace DeepVisionSharp.TorchSharp.Tests;

/// <summary>
/// Trains the YOLO26 detection head on generated die images and checks the boxes come back.
/// </summary>
/// <remarks>
/// Not for routine runs. One pass trains a real detection network for 30 epochs. The project
/// excludes the "Slow" trait by default; run it with <c>dotnet test --filter "Category=Slow"</c>.
/// </remarks>
[Trait("Category", "Slow")]
public sealed class GeneratedDetectionTrainingTests : IDisposable
{
    private const int ImageSize = 128;
    private readonly string _root;

    public GeneratedDetectionTrainingTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "dv-detection-training", Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // A temp folder that survives a test run must not fail the test.
        }
    }

    [Fact]
    public void Training_finds_the_generated_defect_boxes()
    {
        var dataset = Path.Combine(_root, "dataset");
        DatasetGenerator.Generate(new Options(dataset, Seed: 42, Size: ImageSize, TrainPerClass: 16, ValPerClass: 4));
        var samples = LoadSplit(dataset, "train").Concat(LoadSplit(dataset, "val")).ToList();

        using var model = Yolo26DetectModel.Create(inputChannels: 1, numClasses: 2);
        using var optimizer = torch.optim.Adam(model.parameters(), lr: 1e-3);

        for (var epoch = 0; epoch < 30; epoch++)
        {
            model.train();
            var epochLoss = 0f;
            var batches = 0;
            foreach (var batch in samples.Chunk(4))
            {
                using var images = Stack(batch);
                using var loss = DetectionLoss.Compute(model.forward(images), batch.Select(sample => sample.Boxes).ToList(), strides: 8);
                epochLoss += loss.item<float>();
                batches++;
                optimizer.zero_grad();
                loss.backward();
                optimizer.step();
            }

        }

        model.eval();
        var hits = 0;
        var expected = 0;
        foreach (var sample in samples.Where(sample => sample.Boxes.Count > 0))
        {
            expected++;
            using var image = Stack([sample]);
            var found = model.forward(image)
                .SelectMany((prediction, scale) => DetectionDecoder.Decode(prediction, stride: 8 << scale, scoreThreshold: 0.5f))
                .ToList();
            var truth = sample.Boxes[0];
            if (found.Any(detection => detection.ClassIndex == truth.ClassIndex && Overlaps(detection, truth)))
            {
                hits++;
            }
        }

        // Two classes of defect. Matching more than half shows the boxes were learned, not guessed.
        Assert.True(hits > expected / 2, $"检出 {hits}/{expected}，没有超过一半。");
    }

    private static bool Overlaps(DetectionDecoder.Detection detection, GroundTruthBox truth)
    {
        var x0 = Math.Max(detection.X, truth.X);
        var y0 = Math.Max(detection.Y, truth.Y);
        var x1 = Math.Min(detection.X + detection.Width, truth.X + truth.Width);
        var y1 = Math.Min(detection.Y + detection.Height, truth.Y + truth.Height);
        var intersection = Math.Max(0, x1 - x0) * Math.Max(0, y1 - y0);
        var union = (detection.Width * detection.Height) + (truth.Width * truth.Height) - intersection;
        return union > 0 && intersection / union > 0.3f;
    }

    private static torch.Tensor Stack(IReadOnlyList<Sample> batch)
    {
        var tensors = batch.Select(sample =>
        {
            using var mat = TorchSharpImageTensor.LoadMat(sample.Path, expectedChannels: 1);
            return TorchSharpImageTensor.ToClassificationTensor(mat, ImageSize, ImageSize, torch.CPU);
        }).ToList();
        var stacked = torch.cat(tensors, dim: 0);
        foreach (var tensor in tensors)
        {
            tensor.Dispose();
        }

        return stacked;
    }

    private static List<Sample> LoadSplit(string root, string split)
    {
        var samples = new List<Sample>();
        foreach (var directory in Directory.GetDirectories(Path.Combine(root, split)))
        {
            foreach (var image in Directory.GetFiles(directory, "*.png"))
            {
                var boxes = File.ReadAllLines(Path.ChangeExtension(image, ".txt"))
                    .Where(line => !string.IsNullOrWhiteSpace(line))
                    .Select(ParseBox)
                    .ToList();
                samples.Add(new Sample(image, boxes));
            }
        }

        return samples;
    }

    private static GroundTruthBox ParseBox(string line)
    {
        var parts = line.Split(' ');
        var cx = float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture) * ImageSize;
        var cy = float.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture) * ImageSize;
        var width = float.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture) * ImageSize;
        var height = float.Parse(parts[4], System.Globalization.CultureInfo.InvariantCulture) * ImageSize;
        return new GroundTruthBox(int.Parse(parts[0]), cx - (width / 2f), cy - (height / 2f), width, height);
    }

    private sealed record Sample(string Path, IReadOnlyList<GroundTruthBox> Boxes);
}
