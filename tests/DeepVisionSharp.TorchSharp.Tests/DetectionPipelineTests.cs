using System.Text.Json;
using Xunit;
using static TorchSharp.torch;
using DeepVisionSharp.TorchSharp.Classification.Yolo26;
using DeepVisionSharp.TorchSharp.Detection;
using DeepVisionSharp.TorchSharp.Detection.Training;
using DeepVisionSharp.TorchSharp.Detection.Yolo26;
using DeepVisionSharp.TorchSharp.Training;
using DvsDatasetGen;

namespace DeepVisionSharp.TorchSharp.Tests;

public sealed class DetectionPipelineTests
{
    [Fact]
    public void Letterbox_maps_a_box_to_the_canvas_and_back()
    {
        var letterbox = DetectionLetterbox.For(sourceWidth: 200, sourceHeight: 100, targetWidth: 128, targetHeight: 128);
        var box = new GroundTruthBox(0, 10, 10, 50, 20);

        var canvasBox = letterbox.Transform(box);

        Assert.NotNull(canvasBox);
        Assert.Equal(6.4f, canvasBox!.Value.X, precision: 3);
        Assert.Equal(38.4f, canvasBox.Value.Y, precision: 3);
        Assert.Equal(32f, canvasBox.Value.Width, precision: 3);
        Assert.Equal(12.8f, canvasBox.Value.Height, precision: 3);

        var restored = letterbox.ToSource(new ScoredDetection(
            box.ClassIndex, canvasBox.Value.X, canvasBox.Value.Y,
            canvasBox.Value.Width, canvasBox.Value.Height, 0.9f));

        Assert.NotNull(restored);
        Assert.Equal(box.X, restored!.Value.X, precision: 2);
        Assert.Equal(box.Y, restored.Value.Y, precision: 2);
        Assert.Equal(box.Width, restored.Value.Width, precision: 2);
        Assert.Equal(box.Height, restored.Value.Height, precision: 2);
    }

    [Fact]
    public void Nms_suppresses_overlaps_per_class_and_respects_the_cap()
    {
        var boxes = new[]
        {
            new ScoredDetection(0, 0, 0, 10, 10, 0.9f),
            new ScoredDetection(0, 1, 1, 10, 10, 0.8f),
            new ScoredDetection(1, 0, 0, 10, 10, 0.7f),
            new ScoredDetection(0, 50, 50, 10, 10, 0.6f)
        };

        var kept = DetectionNms.Apply(boxes, iouThreshold: 0.5f, maxDetections: 100);

        Assert.Equal(3, kept.Count);
        Assert.DoesNotContain(kept, item => item is { ClassIndex: 0, Score: 0.8f });
        Assert.Contains(kept, item => item is { ClassIndex: 1, Score: 0.7f });

        var capped = DetectionNms.Apply(boxes, iouThreshold: 0.5f, maxDetections: 1);
        Assert.Single(capped);
        Assert.Equal(0.9f, capped[0].Score);
    }

    [Fact]
    public void Metrics_reach_one_for_a_perfect_prediction()
    {
        var truth = new[] { new GroundTruthBox(0, 10, 10, 20, 20) };
        var prediction = new[] { new ScoredDetection(0, 10, 10, 20, 20, 0.95f) };

        var evaluation = DetectionMetrics.Evaluate(
            [new DetectionImage(truth, prediction)],
            ["scratch"]);

        Assert.Equal(1, evaluation.Map50, precision: 5);
        Assert.Equal(1, evaluation.Map5095, precision: 5);
        var metric = Assert.Single(evaluation.PerClass);
        Assert.Equal(1, metric.Precision, precision: 5);
        Assert.Equal(1, metric.Recall, precision: 5);
        Assert.Equal(1, metric.F1, precision: 5);
    }

    [Fact]
    public void Metrics_are_zero_when_nothing_matches()
    {
        var truth = new[] { new GroundTruthBox(0, 10, 10, 20, 20) };
        var prediction = new[] { new ScoredDetection(0, 100, 100, 20, 20, 0.95f) };

        var evaluation = DetectionMetrics.Evaluate(
            [new DetectionImage(truth, prediction)],
            ["scratch"]);

        Assert.Equal(0, evaluation.Map50);
        Assert.Equal(0, evaluation.PerClass[0].Recall);
    }

    [Fact]
    public void Dataset_loader_accepts_missing_label_files_as_negatives()
    {
        var root = Path.Combine(Path.GetTempPath(), "dv-det-dataset", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "train", "images"));
        Directory.CreateDirectory(Path.Combine(root, "train", "labels"));
        Directory.CreateDirectory(Path.Combine(root, "val", "images"));
        Directory.CreateDirectory(Path.Combine(root, "val", "labels"));
        try
        {
            File.WriteAllText(Path.Combine(root, "dataset-manifest.json"),
                """{ "classes": ["scratch"], "dataset_fingerprint": "abc", "task": "detect" }""");
            File.WriteAllBytes(Path.Combine(root, "train", "images", "a.png"), [1, 2, 3]);
            File.WriteAllText(Path.Combine(root, "train", "labels", "a.txt"), "0 0.5 0.5 0.2 0.2\n");
            File.WriteAllBytes(Path.Combine(root, "val", "images", "b.png"), [1, 2, 3]);

            var dataset = DetectionFolderDataset.Load(root);

            Assert.Equal(["scratch"], dataset.Classes);
            Assert.Single(dataset.Train);
            var negative = Assert.Single(dataset.Val);
            Assert.False(File.Exists(negative.LabelPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Detection_model_exposes_all_scales_and_sets_num_classes()
    {
        using var model = Yolo26DetectFactory.Create("yolo26s-det", inputChannels: 3, numClasses: 2);

        Assert.Equal(2, model.NumClasses);
        using var input = zeros(1, 3, 64, 64);
        var outputs = model.forward(input);
        try
        {
            Assert.Equal(3, outputs.Length);
        }
        finally
        {
            foreach (var output in outputs)
            {
                output.Dispose();
            }
        }

        using var singleClass = Yolo26DetectFactory.Create("yolo26n-det", 3, 1);
        Assert.Equal(1, singleClass.NumClasses);
    }

    [Theory]
    [InlineData("yolo26n-det", Yolo26DetectFactory.ArchN)]
    [InlineData("yolo26s-det", Yolo26DetectFactory.ArchS)]
    [InlineData("yolo26m-det", Yolo26DetectFactory.ArchM)]
    [InlineData("yolo26l-det", Yolo26DetectFactory.ArchL)]
    [InlineData("yolo26x-det", Yolo26DetectFactory.ArchX)]
    [InlineData("yolo26-s-det", Yolo26DetectFactory.ArchS)]
    [InlineData("yolo26-m-det", Yolo26DetectFactory.ArchM)]
    [InlineData("yolo26l", Yolo26DetectFactory.ArchL)]
    [InlineData("yolo26x", Yolo26DetectFactory.ArchX)]
    public void ResolveArch_maps_all_five_detection_scales_without_falling_back_to_n(
        string input,
        string expected)
    {
        Assert.Equal(expected, Yolo26DetectFactory.ResolveArch(input));
    }

    [Theory]
    [InlineData("yolo26n-cls", true, DeepVisionSharp.TorchSharp.Models.PreprocessingVersion.UnitScaleV1)]
    [InlineData("yolo26x-cls", true, DeepVisionSharp.TorchSharp.Models.PreprocessingVersion.UnitScaleV1)]
    [InlineData("resnet18_v2", true, DeepVisionSharp.TorchSharp.Models.PreprocessingVersion.ImageNetV2)]
    [InlineData("resnet34_v2", true, DeepVisionSharp.TorchSharp.Models.PreprocessingVersion.ImageNetV2)]
    [InlineData("resnet18_v2", false, DeepVisionSharp.TorchSharp.Models.PreprocessingVersion.UnitScaleV1)]
    public void ResolvePreprocessing_keeps_yolo26_cls_on_unit_scale_even_with_pretrained_weights(
        string archName,
        bool hasPretrainedWeights,
        DeepVisionSharp.TorchSharp.Models.PreprocessingVersion expected)
    {
        Assert.Equal(
            expected,
            DeepVisionSharp.TorchSharp.Classification.TorchSharpClassifierFactory.ResolvePreprocessing(
                archName,
                hasPretrainedWeights));
    }

    [Fact]
    public void Detection_scales_have_strictly_increasing_parameter_counts_and_align_m_l_x_backbone_with_classifier()
    {
        using var nano = Yolo26DetectFactory.Create("yolo26n-det", inputChannels: 3, numClasses: 2);
        using var small = Yolo26DetectFactory.Create("yolo26s-det", inputChannels: 3, numClasses: 2);
        using var medium = Yolo26DetectFactory.Create("yolo26m-det", inputChannels: 3, numClasses: 2);
        using var large = Yolo26DetectFactory.Create("yolo26l-det", inputChannels: 3, numClasses: 2);
        using var xlarge = Yolo26DetectFactory.Create("yolo26x-det", inputChannels: 3, numClasses: 2);

        var countN = nano.parameters().Sum(p => p.numel());
        var countS = small.parameters().Sum(p => p.numel());
        var countM = medium.parameters().Sum(p => p.numel());
        var countL = large.parameters().Sum(p => p.numel());
        var countX = xlarge.parameters().Sum(p => p.numel());

        Assert.True(countN < countS, $"Expected n ({countN}) < s ({countS})");
        Assert.True(countS < countM, $"Expected s ({countS}) < m ({countM})");
        Assert.True(countM < countL, $"Expected m ({countM}) < l ({countL})");
        Assert.True(countL < countX, $"Expected l ({countL}) < x ({countX})");

        foreach (var (detModel, scaleKey) in new[] { (medium, "m"), (large, "l"), (xlarge, "x") })
        {
            using var clsModel = Yolo26ClsClassifier.Create(scaleKey, inputChannels: 3, numClasses: 2);
            var detState = detModel.state_dict();
            var clsState = clsModel.state_dict();

            var detStageKeys = detState.Keys
                .Where(k => (k.StartsWith("backbone_2.", StringComparison.Ordinal)
                             || k.StartsWith("backbone_4.", StringComparison.Ordinal))
                            && !k.EndsWith(".num_batches_tracked", StringComparison.Ordinal)
                            && !k.Split('.').Any(seg => seg.StartsWith('_')))
                .Select(k => k["backbone_".Length..])
                .OrderBy(k => k, StringComparer.Ordinal)
                .ToArray();

            var clsStageKeys = clsState.Keys
                .Where(k => (k.StartsWith("model.2.", StringComparison.Ordinal)
                             || k.StartsWith("model.4.", StringComparison.Ordinal))
                            && !k.EndsWith(".num_batches_tracked", StringComparison.Ordinal)
                            && !k.Split('.').Any(seg => seg.StartsWith('_')))
                .Select(k => k["model.".Length..])
                .OrderBy(k => k, StringComparer.Ordinal)
                .ToArray();

            Assert.NotEmpty(detStageKeys);
            Assert.Equal(clsStageKeys, detStageKeys);

            foreach (var suffix in detStageKeys)
            {
                var detShape = detState["backbone_" + suffix].shape;
                var clsShape = clsState["model." + suffix].shape;
                Assert.Equal(clsShape, detShape);
            }
        }
    }

    [Fact]
    public void Nms_keeps_high_score_boxes_from_later_classes_when_first_class_saturates_cap()
    {
        var boxes = new[]
        {
            new ScoredDetection(0, 0, 0, 10, 10, 0.99f),
            new ScoredDetection(0, 30, 0, 10, 10, 0.30f),
            new ScoredDetection(0, 60, 0, 10, 10, 0.29f),
            new ScoredDetection(0, 90, 0, 10, 10, 0.28f),
            new ScoredDetection(0, 120, 0, 10, 10, 0.27f),
            new ScoredDetection(1, 0, 50, 10, 10, 0.95f),
            new ScoredDetection(1, 30, 50, 10, 10, 0.90f)
        };

        var kept = DetectionNms.Apply(boxes, iouThreshold: 0.5f, maxDetections: 3);

        Assert.Equal(3, kept.Count);
        Assert.Contains(kept, b => b is { ClassIndex: 0, Score: 0.99f });
        Assert.Contains(kept, b => b is { ClassIndex: 1, Score: 0.95f });
        Assert.Contains(kept, b => b is { ClassIndex: 1, Score: 0.90f });
    }

    [Fact]
    public void ClsTrainer_sidecar_records_exact_completed_epochs()
    {
        var root = Path.Combine(Path.GetTempPath(), "dv-cls-sidecar-epochs", Guid.NewGuid().ToString("N"));
        try
        {
            var datasetRoot = Path.Combine(root, "dataset");
            var outputRoot = Path.Combine(root, "run");
            DatasetGenerator.Generate(new Options(datasetRoot, Seed: 7, Size: 16, TrainPerClass: 2, ValPerClass: 2));

            var result = TorchSharpClsTrainer.Run(new TorchSharpClsTrainer.Options(
                DatasetRoot: datasetRoot,
                OutputRoot: outputRoot,
                ModelType: "resnet-tiny",
                Epochs: 1,
                BatchSize: 2,
                LearningRate: 0.01f,
                Optimizer: "sgd",
                InputWidth: 32,
                InputHeight: 32,
                InputChannels: 1,
                DeviceName: "cpu"));

            using var bestDoc = JsonDocument.Parse(File.ReadAllText(result.MetadataPath));
            using var lastDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(outputRoot, TorchSharpClsTrainer.LastFileName + ".info")));
            Assert.Equal(1, bestDoc.RootElement.GetProperty("epochs").GetInt32());
            Assert.Equal(1, lastDoc.RootElement.GetProperty("epochs").GetInt32());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
