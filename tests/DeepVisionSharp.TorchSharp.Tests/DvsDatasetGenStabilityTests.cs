using Xunit;
using DvsDatasetGen;

namespace DeepVisionSharp.TorchSharp.Tests;

public sealed class DvsDatasetGenStabilityTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dv-dataset-gen", Guid.NewGuid().ToString("N"));

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
    public void Variant_is_stable_for_the_slow_test_seed()
    {
        // Locked so a future edit cannot swap back to HashCode.Combine, whose salt changes per process.
        Assert.Equal(-1668652539, DatasetGenerator.Variant(42, 0, 0));
        Assert.Equal(1, Math.Abs(DatasetGenerator.Variant(42, 0, 0)) % 7);
        Assert.Equal(1988618140, DatasetGenerator.Variant(42, 1, 0));
        Assert.Equal(-393259305, DatasetGenerator.Variant(42, 2, 0));
    }

    [Fact]
    public void Same_seed_writes_the_same_png_bytes()
    {
        var first = Path.Combine(_root, "a");
        var second = Path.Combine(_root, "b");
        var options = new Options(first, Seed: 42, Size: 16, TrainPerClass: 2, ValPerClass: 1);
        DatasetGenerator.Generate(options);
        DatasetGenerator.Generate(options with { Root = second });

        var left = Directory.GetFiles(first, "*.png", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.Ordinal).ToArray();
        var right = Directory.GetFiles(second, "*.png", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.Ordinal).ToArray();
        Assert.Equal(left.Length, right.Length);
        Assert.NotEmpty(left);
        for (var index = 0; index < left.Length; index++)
        {
            Assert.Equal(Path.GetRelativePath(first, left[index]), Path.GetRelativePath(second, right[index]));
            Assert.Equal(File.ReadAllBytes(left[index]), File.ReadAllBytes(right[index]));
        }
    }
}
