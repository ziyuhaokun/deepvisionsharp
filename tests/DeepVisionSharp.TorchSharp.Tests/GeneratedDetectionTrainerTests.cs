using System.Text.Json;
using Xunit;
using DvsDatasetGen;
using DeepVisionSharp.TorchSharp.Detection;
using DeepVisionSharp.TorchSharp.Detection.Training;

namespace DeepVisionSharp.TorchSharp.Tests;

/// <summary>
/// Trains the production detection trainer over a generated YOLO layout. Not for routine runs:
/// run with <c>dotnet test --filter "Category=Slow"</c>.
/// </summary>
[Trait("Category", "Slow")]
public sealed class GeneratedDetectionTrainerTests : IDisposable
{
    private readonly string _root;

    public GeneratedDetectionTrainerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "dv-det-trainer", Guid.NewGuid().ToString("N"));
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
            // A temp folder that survives the test must not fail it.
        }
    }

    [Fact]
    public void Trainer_writes_a_best_checkpoint_and_detection_sidecar()
    {
        var generated = Path.Combine(_root, "generated");
        DatasetGenerator.Generate(new Options(generated, Seed: 42, Size: 96, TrainPerClass: 6, ValPerClass: 2));
        var dataset = Path.Combine(_root, "detection-dataset");
        BuildDetectionLayout(generated, dataset, DatasetGenerator.Classes);

        var output = Path.Combine(_root, "run");
        var result = TorchSharpDetTrainer.Run(new TorchSharpDetTrainer.Options(
            DatasetRoot: dataset,
            OutputRoot: output,
            ModelType: "yolo26n-det",
            Epochs: 5,
            BatchSize: 4,
            LearningRate: 1e-3f,
            Optimizer: "adam",
            InputWidth: 64,
            InputHeight: 64,
            InputChannels: 3,
            DeviceName: "cpu",
            HorizontalFlip: true,
            EvaluationConfidence: 0.1f,
            NmsIou: 0.5f,
            MaxDetections: 20));

        Assert.True(File.Exists(result.BestModelPath));
        Assert.True(File.Exists(result.MetadataPath));
        using var metadata = JsonDocument.Parse(File.ReadAllText(result.MetadataPath));
        var root = metadata.RootElement;
        Assert.Equal("trained_model", root.GetProperty("artifact_kind").GetString());
        Assert.Equal("yolo26n-det", root.GetProperty("model_type").GetString());
        Assert.Equal("letterbox_v1", root.GetProperty("preprocessing").GetString());
        Assert.Equal(3, root.GetProperty("classes").GetArrayLength());
        Assert.True(root.TryGetProperty("box_map_50", out _));
        Assert.True(result.BestMap50 >= 0);

        // The saved sidecar is what the inference engine rebuilds from: prove the checkpoint loads
        // and decodes boxes back into source pixels.
        var valImage = Directory.GetFiles(Path.Combine(dataset, "val", "images"), "*.png").First();
        using var engine = new TorchSharpDetectEngine();
        var inference = engine.Infer(result.BestModelPath, "cpu", valImage, 0.1, 0.5, 20);
        Assert.Equal(96, inference.ImageWidth);
        Assert.Equal(96, inference.ImageHeight);
        Assert.All(inference.Boxes, box =>
        {
            Assert.True(box.X >= 0 && box.Y >= 0);
            Assert.True(box.Width > 0 && box.Height > 0);
            Assert.True(box.X + box.Width <= inference.ImageWidth + 0.5);
            Assert.True(box.Y + box.Height <= inference.ImageHeight + 0.5);
        });
    }

    private static void BuildDetectionLayout(
        string generatedRoot,
        string destination,
        IReadOnlyList<string> classes)
    {
        foreach (var split in new[] { "train", "val" })
        {
            Directory.CreateDirectory(Path.Combine(destination, split, "images"));
            Directory.CreateDirectory(Path.Combine(destination, split, "labels"));
            var index = 0;
            foreach (var classDirectory in Directory.GetDirectories(Path.Combine(generatedRoot, split)))
            {
                foreach (var image in Directory.GetFiles(classDirectory, "*.png"))
                {
                    var name = $"{index++:D5}";
                    File.Copy(image, Path.Combine(destination, split, "images", name + ".png"));
                    var label = Path.ChangeExtension(image, ".txt");
                    var target = Path.Combine(destination, split, "labels", name + ".txt");
                    if (File.Exists(label))
                    {
                        File.Copy(label, target);
                    }
                    else
                    {
                        File.WriteAllText(target, string.Empty);
                    }
                }
            }
        }

        File.WriteAllText(
            Path.Combine(destination, "dataset-manifest.json"),
            JsonSerializer.Serialize(new
            {
                schema_version = 1,
                task = "detect",
                classes,
                dataset_fingerprint = "generated-detection",
                split_method = "generated-v1"
            }));
    }
}
