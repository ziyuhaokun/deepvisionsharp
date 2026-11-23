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
using DeepVisionSharp.TorchSharp.Classification.Pretrained;
using DeepVisionSharp.TorchSharp.Detection;
using DeepVisionSharp.TorchSharp.Detection.Training;
using DeepVisionSharp.TorchSharp.Models;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCvSharp;
using Xunit;

namespace DeepVisionSharp.Infrastructure.Tests;

[CollectionDefinition(nameof(DetectionModelLibraryTests), DisableParallelization = true)]
public sealed class DetectionModelLibraryCollection;

[Collection(nameof(DetectionModelLibraryTests))]
public sealed class DetectionModelLibraryTests
{
    [Theory]
    [InlineData("yolo26n", 16, false, "detection_pretrained")]
    [InlineData("yolo26n-cls", 16, false, "classification_backbone")]
    [InlineData("yolo26n", 5, false, "random")]
    [InlineData("yolo26n", 16, true, "random")]
    public async Task Saves_and_runs_a_detector_using_the_original_run_settings(
        string bundleArch, int filters, bool corrupt, string expectedInitialization)
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(repository.FullName, "DeepVisionSharp.slnx")))
        {
            repository = repository.Parent ?? throw new DirectoryNotFoundException("DeepVisionSharp.slnx");
        }

        var root = Directory.CreateDirectory(Path.Combine(repository.FullName, "temp", $"detection-save-{Guid.NewGuid():N}"));
        try
        {
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
                Name = "Detection model round trip",
                Type = ProjectTypes.Detection,
                CreatedUtc = DateTime.UtcNow,
                LastUpdatedUtc = DateTime.UtcNow
            }).ExecuteAffrowsAsync();

            var store = services.GetRequiredService<IProjectFileStore>();
            var output = new TrainingWorkspacePlanner(store).CreateRunPlan(projectId, jobId, "Detection").OutputRoot;
            var dataset = Path.Combine(output, "detection-dataset");
            var image = WriteDataset(dataset);
            var cache = Path.Combine(root.FullName, "pretrained", bundleArch);
            var values = new float[filters * 3 * 3 * 3];
            var random = new Random(42);
            for (var index = 0; index < values.Length; index++)
            {
                values[index] = random.NextSingle() * 0.2f - 0.1f;
            }
            var bytes = new byte[values.Length * sizeof(float)];
            Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
            var tensor = new RawTensor("model.0.conv.weight", [filters, 3, 3, 3], "float32", bytes);
            PretrainedBundleWriter.Write([tensor], cache, bundleArch);
            if (corrupt)
            {
                File.Delete(Path.Combine(cache, "0000.bin"));
            }

            var messages = new List<string>();
            const int maxDetections = 7;
            const float nmsIou = 0.55f;
            global::TorchSharp.torch.manual_seed(42);
            var trained = TorchSharpDetTrainer.Run(new TorchSharpDetTrainer.Options(
                dataset, output, ModelTypeNames.Yolo26NDet, 1, 2, 0.001f, "adam", 96, 64,
                DeviceName: "cpu", HorizontalFlip: false, PretrainedDirectory: cache,
                EvaluationConfidence: 0.4f, NmsIou: nmsIou, MaxDetections: maxDetections,
                Log: messages.Add, TrainingJobId: jobId));
            var run = DetectionTrainingRun.Read(output);
            Assert.Equal(jobId, run.TrainingJobId);
            Assert.Equal(expectedInitialization, run.Initialization);
            var randomFallback = expectedInitialization == "random";
            Assert.Equal(randomFallback ? 0 : 1, run.PretrainedLoadedTensors);
            Assert.Equal(randomFallback ? null : bundleArch, run.PretrainedArch);
            Assert.Equal(randomFallback, messages.Any(line => line.Contains("回退随机初始化", StringComparison.Ordinal)));
            Assert.Equal(new[] { "dent", "scratch" }, run.Classes);
            Assert.Equal("detection-round-trip", run.DatasetFingerprint);

            var lastPath = Path.Combine(output, TorchSharpDetTrainer.LastFileName);
            using var engine = new TorchSharpDetectEngine();
            var originalBest = await File.ReadAllTextAsync(trained.BestModelPath + ".info");
            var expected = new[]
            {
                (Selection: TrainingCheckpointSelection.Best, Path: trained.BestModelPath),
                (Selection: TrainingCheckpointSelection.Last, Path: lastPath)
            };
            await services.GetRequiredService<ITrainingJobRepository>().SaveAsync(new TrainingJob(
                jobId, projectId, "Train", BackendKeys.TorchSharpDetection, ModelTypeNames.Yolo26NDet,
                dataset, output, TrainingJobStatus.Cancelled, DateTime.UtcNow, DateTime.UtcNow,
                trained.BestModelPath, lastPath, null, null,
                new TrainingConfiguration(1, 2, 0.001, "adam", networkInputWidth: 64, networkInputHeight: 64,
                    detectionOptions: new DetectionTrainingOptions(MaxDetections: 50)), null));
            Directory.Delete(cache, recursive: true);
            var mediator = services.GetRequiredService<IMediator>();
            var models = services.GetRequiredService<IModelRepository>();
            foreach (var (selection, source) in expected)
            {
                var before = engine.Infer(source, "cpu", image, confidenceThreshold: 0, nmsIou: 0, maxDetections: 0);
                Assert.NotEmpty(before.Boxes);
                if (selection == TrainingCheckpointSelection.Best)
                {
                    File.Delete(source + ".info");
                }
                else
                {
                    await File.WriteAllTextAsync(source + ".info", "{invalid metadata");
                }

                var saved = await mediator.Send(new SaveTrainedModelCommand(projectId, jobId, $"Recovered {selection}",
                    TargetModelId: null, CheckpointSelection: selection));
                Assert.True(saved.IsSuccess, saved.Error);
                var model = await models.GetByIdAsync(saved.Value);
                Assert.NotNull(model);
                Assert.Equal(TrainedModelStatus.Ready, model.Status);
                Assert.Equal(run.Classes, model.Classes);
                Assert.Equal(maxDetections, model.MaxDetections);
                var bundle = TorchSharpModelBundle.Load(model.CheckpointPath, "detect");
                Assert.Equal(PreprocessingVersion.LetterboxV1, bundle.Preprocessing);
                Assert.Equal((96, 64, 3), (bundle.InputWidth, bundle.InputHeight, bundle.InputChannels));
                Assert.Equal(new[] { 8, 16, 32 }, bundle.Strides);
                Assert.Equal(nmsIou, bundle.NmsIou, precision: 6);
                Assert.Equal(maxDetections, bundle.MaxDetections);
                using var info = JsonDocument.Parse(await File.ReadAllTextAsync(model.CheckpointPath + ".info"));
                Assert.True(info.RootElement.GetProperty("sppf_shortcut").GetBoolean());
                Assert.Equal(expectedInitialization, info.RootElement.GetProperty("initialization").GetString());
                Assert.Equal(run.DatasetFingerprint, info.RootElement.GetProperty("dataset_fingerprint").GetString());
                Assert.False(info.RootElement.TryGetProperty("epochs", out _));
                Assert.False(info.RootElement.TryGetProperty("box_map_50", out _));

                var after = engine.Infer(model.CheckpointPath, "cpu", image, confidenceThreshold: 0, nmsIou: 0, maxDetections: 0);
                Assert.Equal((before.ImageWidth, before.ImageHeight), (after.ImageWidth, after.ImageHeight));
                Assert.Equal(before.Boxes.Select(BoxValues), after.Boxes.Select(BoxValues));
            }

            var runPath = Path.Combine(output, DetectionTrainingRun.FileName);
            var wrongRun = JsonNode.Parse(await File.ReadAllTextAsync(runPath))!.AsObject();
            wrongRun["training_job_id"] = Guid.NewGuid();
            await File.WriteAllTextAsync(runPath, wrongRun.ToJsonString());
            var rejected = await mediator.Send(new SaveTrainedModelCommand(projectId, jobId, "Wrong run", TargetModelId: null));
            Assert.True(rejected.IsFailure);
            Assert.Contains("当前任务", rejected.Error);

            File.Delete(runPath);
            var missing = await mediator.Send(new SaveTrainedModelCommand(projectId, jobId, "No run", TargetModelId: null));
            Assert.True(missing.IsFailure);
            Assert.Contains(DetectionTrainingRun.FileName, missing.Error);

            var legacy = JsonNode.Parse(originalBest)!.AsObject();
            foreach (var property in new[] { "initialization", "pretrained_arch", "pretrained_loaded_tensors" })
            {
                legacy.Remove(property);
            }
            var legacyText = legacy.ToJsonString();
            await File.WriteAllTextAsync(trained.BestModelPath + ".info", legacyText);
            var savedLegacy = await mediator.Send(new SaveTrainedModelCommand(projectId, jobId, "Legacy", TargetModelId: null));
            Assert.True(savedLegacy.IsSuccess, savedLegacy.Error);
            var legacyModel = await models.GetByIdAsync(savedLegacy.Value);
            Assert.NotNull(legacyModel);
            Assert.Equal(legacyText, await File.ReadAllTextAsync(legacyModel.CheckpointPath + ".info"));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    private static (string ClassName, double Confidence, double X, double Y, double Width, double Height) BoxValues(DetectionBoxResult box) =>
        (box.ClassName, box.Confidence, box.X, box.Y, box.Width, box.Height);

    private static string WriteDataset(string root)
    {
        foreach (var split in new[] { "train", "val" })
        {
            var images = Directory.CreateDirectory(Path.Combine(root, split, "images")).FullName;
            var labels = Directory.CreateDirectory(Path.Combine(root, split, "labels")).FullName;
            for (var index = 0; index < 2; index++)
            {
                using var image = new Mat(72, 88, MatType.CV_8UC3, new Scalar(30, 90, 130));
                Cv2.Rectangle(image, new Rect(22, 18, 44, 36), new Scalar(160, 105, 180), thickness: -1);
                Assert.True(Cv2.ImWrite(Path.Combine(images, $"sample{index}.png"), image));
                File.WriteAllText(Path.Combine(labels, $"sample{index}.txt"), $"{index} 0.5 0.5 0.5 0.5\n");
            }
        }

        File.WriteAllText(Path.Combine(root, "dataset-manifest.json"),
            """{"schema_version":1,"task":"detect","classes":["dent","scratch"],"dataset_fingerprint":"detection-round-trip"}""");
        return Path.Combine(root, "val", "images", "sample0.png");
    }
}
