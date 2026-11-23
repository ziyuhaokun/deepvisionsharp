using System.Text.Json;
using System.Text.Json.Nodes;
using DeepVisionSharp.Application;
using DeepVisionSharp.Application.Models;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Domain.Entities;
using DeepVisionSharp.Domain.Repositories;
using DeepVisionSharp.Domain.ValueObjects;
using DeepVisionSharp.Infrastructure.Modules;
using DeepVisionSharp.Infrastructure.Persistence;
using DeepVisionSharp.Infrastructure.Services;
using DeepVisionSharp.TorchSharp.Classification;
using DeepVisionSharp.TorchSharp.Classification.Pretrained;
using DeepVisionSharp.TorchSharp.Models;
using DeepVisionSharp.TorchSharp.Training;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCvSharp;
using Xunit;

namespace DeepVisionSharp.Infrastructure.Tests;

[CollectionDefinition(nameof(ClassificationModelLibraryTests), DisableParallelization = true)]
public sealed class ClassificationModelLibraryCollection;

[Collection(nameof(ClassificationModelLibraryTests))]
public sealed class ClassificationModelLibraryTests
{
    [Theory]
    [InlineData(true, PreprocessingVersion.ImageNetV2)]
    [InlineData(false, PreprocessingVersion.UnitScaleV1)]
    public async Task Saving_to_the_model_library_uses_the_training_record_after_the_cache_changes(
        bool validWeights, PreprocessingVersion expectedPreprocessing)
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(repository.FullName, "DeepVisionSharp.slnx")))
        {
            repository = repository.Parent ?? throw new DirectoryNotFoundException("DeepVisionSharp.slnx");
        }

        var root = Directory.CreateDirectory(Path.Combine(repository.FullName, "temp", $"classification-save-{Guid.NewGuid():N}"));
        const string weightsVariable = "DV_PRETRAINED_ROOT";
        var previousRoot = Environment.GetEnvironmentVariable(weightsVariable);
        try
        {
            var cache = Path.Combine(root.FullName, "pretrained");
            Environment.SetEnvironmentVariable(weightsVariable, cache);
            var weights = Path.Combine(cache, "resnet18");
            WriteWeights(weights, validWeights);

            using var services = new ServiceCollection()
                .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
                .AddSingleton<IDataRootProvider>(new DefaultDataRootProvider(Path.Combine(root.FullName, "data")))
                .AddDeepVisionApplication()
                .AddDataModule()
                .AddModelArtifactsModule()
                .BuildServiceProvider();
            using var db = services.GetRequiredService<SqliteDb>().Instance;
            var projectId = Guid.NewGuid();
            var jobId = Guid.NewGuid();
            await db.Insert(new SqlProject
            {
                Id = projectId,
                Name = "Model library round trip",
                Type = ProjectTypes.Classification,
                CreatedUtc = DateTime.UtcNow,
                LastUpdatedUtc = DateTime.UtcNow
            }).ExecuteAffrowsAsync();

            var fileStore = services.GetRequiredService<IProjectFileStore>();
            var output = new TrainingWorkspacePlanner(fileStore).CreateRunPlan(projectId, jobId, "Train").OutputRoot;
            var dataset = Path.Combine(output, "classification-dataset");
            var image = WriteDataset(dataset);
            var messages = new List<string>();
            var started = DateTime.UtcNow;
            var trained = TorchSharpClsTrainer.Run(new TorchSharpClsTrainer.Options(
                dataset, output, ModelTypeNames.ResNetTiny, 1, 2, 0.005f, "adamw", 64, 48,
                DeviceName: "cpu", PretrainedDirectory: PretrainedWeightsLocator.Resolve(TorchSharpClassifierFactory.ArchResNetTiny),
                Log: messages.Add, UseAutomaticPreprocessing: true, TrainingJobId: jobId));
            var lastPath = Path.Combine(output, TorchSharpClsTrainer.LastFileName);
            var run = ClassificationTrainingRun.Read(output);
            Assert.Equal(expectedPreprocessing.ToWireValue(), run.Preprocessing);
            Assert.Equal(validWeights ? 5 : 0, run.PretrainedLoadedTensors);
            Assert.Equal(validWeights ? "resnet18" : null, run.Pretrained);
            Assert.Equal(validWeights ? "resnet18" : null, run.PretrainedArch);
            Assert.Equal(!validWeights, messages.Any(line => line.Contains("回退随机初始化", StringComparison.Ordinal)));
            Assert.Equal(new[] { "NG", "OK" }, run.Classes);
            Assert.Equal("library-round-trip", run.DatasetFingerprint);
            Assert.Equal(jobId, run.TrainingJobId);

            using var engine = new TorchSharpClsEngine();
            var expected = new Dictionary<TrainingCheckpointSelection, (string Path, ClassificationResult Prediction)>
            {
                [TrainingCheckpointSelection.Best] = (trained.BestModelPath, engine.Infer(trained.BestModelPath, "cpu", image, 0).Detection!),
                [TrainingCheckpointSelection.Last] = (lastPath, engine.Infer(lastPath, "cpu", image, 0).Detection!)
            };
            await services.GetRequiredService<ITrainingJobRepository>().SaveAsync(new TrainingJob(
                jobId, projectId, "Train", BackendKeys.TorchSharpClassification, ModelTypeNames.ResNetTiny,
                dataset, output, TrainingJobStatus.Cancelled, started, DateTime.UtcNow, trained.BestModelPath, lastPath,
                null, null, new TrainingConfiguration(1, 2, 0.005, "adamw", networkInputWidth: 64, networkInputHeight: 48), null));

            // Flip what the former recovery code would infer from the current weights cache.
            Directory.Delete(weights, recursive: true);
            if (!validWeights)
            {
                WriteWeights(weights, valid: true);
            }

            var mediator = services.GetRequiredService<IMediator>();
            var originalSidecar = await File.ReadAllTextAsync(trained.BestModelPath + ".info");
            foreach (var (selection, source) in expected)
            {
                File.Delete(source.Path + ".info");
                var saved = await mediator.Send(new SaveTrainedModelCommand(projectId, jobId, $"Saved {selection}",
                    TargetModelId: null, CheckpointSelection: selection));
                Assert.True(saved.IsSuccess, saved.Error);
                var model = await services.GetRequiredService<IModelRepository>().GetByIdAsync(saved.Value);
                Assert.NotNull(model);
                Assert.Equal(TrainedModelStatus.Ready, model.Status);
                Assert.Equal(run.Classes, model.Classes);
                var bundle = TorchSharpModelBundle.Load(model.CheckpointPath, "classify");
                Assert.Equal(expectedPreprocessing, bundle.Preprocessing);
                Assert.Equal((64, 48, 3), (bundle.InputWidth, bundle.InputHeight, bundle.InputChannels));
                using var recovered = JsonDocument.Parse(await File.ReadAllTextAsync(model.CheckpointPath + ".info"));
                Assert.Equal(run.DatasetFingerprint, recovered.RootElement.GetProperty("dataset_fingerprint").GetString());
                Assert.Equal(validWeights ? "pretrained" : "random", recovered.RootElement.GetProperty("initialization").GetString());
                Assert.Equal(run.PretrainedLoadedTensors, recovered.RootElement.GetProperty("pretrained_loaded_tensors").GetInt32());
                Assert.False(recovered.RootElement.TryGetProperty("epochs", out _));
                var result = engine.Infer(model.CheckpointPath, "cpu", image, 0);
                Assert.NotNull(result.Detection);
                Assert.Equal(source.Prediction.ClassName, result.Detection.ClassName);
                Assert.Equal(source.Prediction.Confidence, result.Detection.Confidence, precision: 5);
            }

            var runPath = Path.Combine(output, ClassificationTrainingRun.FileName);
            var mismatchedRun = JsonNode.Parse(await File.ReadAllTextAsync(runPath))!.AsObject();
            mismatchedRun["training_job_id"] = Guid.NewGuid();
            await File.WriteAllTextAsync(runPath, mismatchedRun.ToJsonString());
            var mismatch = await mediator.Send(new SaveTrainedModelCommand(projectId, jobId, "Wrong run", TargetModelId: null));
            Assert.True(mismatch.IsFailure);
            Assert.Contains("当前任务", mismatch.Error);

            File.Delete(runPath);
            var missing = await mediator.Send(new SaveTrainedModelCommand(projectId, jobId, "Missing run", TargetModelId: null));
            Assert.True(missing.IsFailure);
            Assert.Contains(ClassificationTrainingRun.FileName, missing.Error);

            var legacySidecar = JsonNode.Parse(originalSidecar)!.AsObject();
            legacySidecar["model_type"] = TorchSharpClassifierFactory.ArchResNetTiny;
            foreach (var key in new[] { "task", "initialization", "pretrained", "pretrained_arch", "pretrained_loaded_tensors" })
            {
                legacySidecar.Remove(key);
            }
            var legacyText = legacySidecar.ToJsonString();
            await File.WriteAllTextAsync(trained.BestModelPath + ".info", legacyText);
            var legacy = await mediator.Send(new SaveTrainedModelCommand(projectId, jobId, "Legacy sidecar", TargetModelId: null));
            Assert.True(legacy.IsSuccess, legacy.Error);
            var storedLegacy = await services.GetRequiredService<IModelRepository>().GetByIdAsync(legacy.Value);
            Assert.NotNull(storedLegacy);
            Assert.Equal(legacyText, await File.ReadAllTextAsync(storedLegacy.CheckpointPath + ".info"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(weightsVariable, previousRoot);
            root.Delete(recursive: true);
        }
    }

    private static string WriteDataset(string root)
    {
        foreach (var split in new[] { "train", "val" })
        {
            for (var category = 0; category < 2; category++)
            {
                var directory = Directory.CreateDirectory(Path.Combine(root, split, category.ToString("D4"))).FullName;
                using var image = new Mat(72, 64, MatType.CV_8UC3, new Scalar(32, 72 + (category * 120), 210));
                Cv2.Rectangle(image, new Rect(10 + (category * 25), 12, 23, 32), new Scalar(180, 110, 100), thickness: -1);
                Assert.True(Cv2.ImWrite(Path.Combine(directory, "image.png"), image));
            }
        }

        File.WriteAllText(Path.Combine(root, "dataset-manifest.json"),
            """{"schema_version":1,"classes":["NG","OK"],"dataset_fingerprint":"library-round-trip"}""");
        return Path.Combine(root, "val", "0000", "image.png");
    }

    private static void WriteWeights(string directory, bool valid)
    {
        var tensors = new[]
        {
            Weights("conv1.weight", [16, 3, 7, 7]),
            Weights("layer1.0.conv1.weight", [16, 16, 3, 3]),
            Weights("layer2.0.conv1.weight", [32, 16, 3, 3]),
            Weights("layer3.0.conv1.weight", [64, 32, 3, 3]),
            Weights("layer4.0.conv1.weight", valid ? [128, 64, 3, 3] : [128, 64, 2, 2])
        };
        PretrainedBundleWriter.Write(tensors, directory, "resnet18");
    }

    private static RawTensor Weights(string name, long[] shape)
    {
        var values = new float[shape.Aggregate(1L, (product, size) => product * size)];
        Array.Fill(values, 0.01f);
        var data = new byte[values.Length * sizeof(float)];
        Buffer.BlockCopy(values, 0, data, 0, data.Length);
        return new RawTensor(name, shape, "float32", data);
    }
}
