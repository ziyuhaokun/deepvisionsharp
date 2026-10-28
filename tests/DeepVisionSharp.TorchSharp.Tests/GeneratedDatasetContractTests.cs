using Xunit;
using DeepVisionSharp.TorchSharp.Training;
using DvsDatasetGen;

namespace DeepVisionSharp.TorchSharp.Tests;

/// <summary>
/// The generated dataset must come back out of the real loader exactly as it was written:
/// class order, split sizes, and one directory per class.
/// </summary>
public sealed class GeneratedDatasetContractTests : IDisposable
{
    private const int TrainPerClass = 4;
    private const int ValPerClass = 2;
    private readonly string _root;

    public GeneratedDatasetContractTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "dv-dataset-contract", Guid.NewGuid().ToString("N"));
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
    public void Loader_reads_back_the_classes_and_counts_the_generator_wrote()
    {
        DatasetGenerator.Generate(new Options(_root, Seed: 42, Size: 64, TrainPerClass, ValPerClass));

        var dataset = ClassificationFolderDataset.Load(_root);

        Assert.Equal(DatasetGenerator.Classes, dataset.Classes);
        AssertSplit(dataset.TrainSamples, TrainPerClass);
        AssertSplit(dataset.ValSamples, ValPerClass);
        Assert.False(string.IsNullOrWhiteSpace(dataset.Fingerprint));
    }

    private static void AssertSplit(IReadOnlyList<ClassificationFolderDataset.Sample> samples, int perClass)
    {
        Assert.Equal(DatasetGenerator.Classes.Length * perClass, samples.Count);
        for (var classIndex = 0; classIndex < DatasetGenerator.Classes.Length; classIndex++)
        {
            var index = classIndex;
            var inClass = samples.Where(sample => sample.ClassIndex == index).ToList();
            Assert.Equal(perClass, inClass.Count);
            Assert.All(inClass, sample => Assert.True(File.Exists(sample.ImagePath)));
        }
    }
}
