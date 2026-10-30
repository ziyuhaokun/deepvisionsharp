using Xunit;
using static TorchSharp.torch;
using DeepVisionSharp.TorchSharp.Detection;
using DeepVisionSharp.TorchSharp.Detection.Training;
using DeepVisionSharp.TorchSharp.Detection.Yolo26;

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
}
