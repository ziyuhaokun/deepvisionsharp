using TorchSharp;
using Xunit;
using DeepVisionSharp.TorchSharp.Classification;
using DeepVisionSharp.TorchSharp.Training;
using DvsDatasetGen;

namespace DeepVisionSharp.TorchSharp.Tests;

/// <summary>
/// 分类闭环的快测：真训练 1 轮，产出的权重和 .info 直接交给推理引擎，预测的类名必须是训练时的类名。
/// 不看准确率（1 轮学不到东西），只证明「训练写出的东西，推理读得回来且对得上」。
/// </summary>
public sealed class ClassificationTrainToInferTests : IDisposable
{
    private readonly string _root;

    public ClassificationTrainToInferTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "dv-train-to-infer", Guid.NewGuid().ToString("N"));
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
    public void Trained_checkpoint_loads_in_the_engine_and_predicts_a_training_class()
    {
        var datasetRoot = Path.Combine(_root, "dataset");
        var outputRoot = Path.Combine(_root, "run");
        DatasetGenerator.Generate(new Options(datasetRoot, Seed: 42, Size: 64, TrainPerClass: 4, ValPerClass: 2));

        torch.manual_seed(42);
        var trained = TorchSharpClsTrainer.Run(new TorchSharpClsTrainer.Options(
            DatasetRoot: datasetRoot,
            OutputRoot: outputRoot,
            ModelType: "resnet_tiny_v2",
            Epochs: 1,
            BatchSize: 4,
            LearningRate: 0.005f,
            Optimizer: "adamw",
            InputWidth: 64,
            InputHeight: 64,
            InputChannels: 1,
            DeviceName: "cpu"));

        // 训练写出的权重和 sidecar 都在，且类序就是数据集的类序。
        Assert.True(File.Exists(trained.BestModelPath));
        Assert.True(File.Exists(trained.BestModelPath + ".info"));
        Assert.Equal(DatasetGenerator.Classes, trained.Classes);

        var image = Directory
            .EnumerateFiles(Path.Combine(datasetRoot, "val"), "*.*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .First();

        using var engine = new TorchSharpClsEngine();
        var result = engine.Infer(trained.BestModelPath, "cpu", image, 0.0);

        // 推理端按 sidecar 取类名：必须是训练时的类名之一，置信度在合法范围。
        Assert.NotNull(result.Detection);
        Assert.Contains(result.Detection!.ClassName, DatasetGenerator.Classes);
        Assert.InRange(result.Detection.Confidence, 0.0, 1.0);
        Assert.Equal(64, result.ImageWidth);
        Assert.Equal(64, result.ImageHeight);

        // last 检查点同样要能被引擎读回来（「保存为模型」可能选它）。
        var lastPath = Path.Combine(outputRoot, "last.torchsharp");
        var lastResult = engine.Infer(lastPath, "cpu", image, 0.0);
        Assert.Contains(lastResult.Detection!.ClassName, DatasetGenerator.Classes);
    }
}
