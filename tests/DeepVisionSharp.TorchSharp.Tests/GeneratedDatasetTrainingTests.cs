using Xunit;
using DeepVisionSharp.TorchSharp.Training;
using DvsDatasetGen;

namespace DeepVisionSharp.TorchSharp.Tests;

/// <summary>
/// Trains the smallest YOLO26 classifier on a generated dataset and checks it beats chance.
/// </summary>
/// <remarks>
/// Not for routine runs. One pass trains a real network for 8 epochs and costs well over a minute,
/// so the project excludes the "Slow" trait by default. Run it on purpose:
/// <c>dotnet test --filter "Category=Slow"</c>.
/// </remarks>
[Trait("Category", "Slow")]
public sealed class GeneratedDatasetTrainingTests : IDisposable
{
    private readonly string _root;

    public GeneratedDatasetTrainingTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "dv-dataset-training", Guid.NewGuid().ToString("N"));
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
    public void Training_on_the_generated_dataset_beats_chance()
    {
        var datasetRoot = Path.Combine(_root, "dataset");
        var outputRoot = Path.Combine(_root, "run");
        // More images than the contract fixture: 4 per class is not enough to learn from.
        DatasetGenerator.Generate(new Options(datasetRoot, Seed: 42, Size: 64, TrainPerClass: 24, ValPerClass: 8));

        var result = TorchSharpClsTrainer.Run(new TorchSharpClsTrainer.Options(
            DatasetRoot: datasetRoot,
            OutputRoot: outputRoot,
            ModelType: "yolo26n-cls",
            Epochs: 8,
            BatchSize: 8,
            LearningRate: 0.05f,
            Optimizer: "adamw",
            InputWidth: 64,
            InputHeight: 64,
            InputChannels: 1,
            DeviceName: "cpu"));

        // Three classes, so a third is chance. Clearing it by a margin shows the marks were learnable.
        Assert.True(
            result.BestValAccuracy > 0.6f,
            $"验证准确率 {result.BestValAccuracy:P0} 没有超过随机水平。");
        Assert.True(File.Exists(result.BestModelPath));
        Assert.Equal(DatasetGenerator.Classes, result.Classes);
    }
}
