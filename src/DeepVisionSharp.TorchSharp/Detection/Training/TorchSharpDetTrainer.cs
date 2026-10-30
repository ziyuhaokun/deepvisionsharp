using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

using OpenCvSharp;
using TorchSharp;
using static TorchSharp.torch;

using DeepVisionSharp.Domain.Datasets;
using DeepVisionSharp.TorchSharp.Detection.Yolo26;
using DeepVisionSharp.TorchSharp.Device;
using DeepVisionSharp.TorchSharp.IO;
using DeepVisionSharp.TorchSharp.Models;
using DeepVisionSharp.TorchSharp.Tensors;
using DeepVisionSharp.TorchSharp.Training;
using CvScalar = OpenCvSharp.Scalar;
using CvSize = OpenCvSharp.Size;

namespace DeepVisionSharp.TorchSharp.Detection.Training;

/// <summary>
/// In-process YOLO26 detection training. Mirrors the classification trainer's contract
/// (atomic best/last checkpoints, metrics sidecar, cancellation, early stopping) but trains the
/// three-scale detect head with <see cref="DetectionLoss"/>.
/// </summary>
public static class TorchSharpDetTrainer
{
    public const string BestFileName = "best.torchsharp";
    public const string LastFileName = "last.torchsharp";
    public const string WeightsFormat = "torchsharp";

    public sealed record Options(
        string DatasetRoot,
        string OutputRoot,
        string ModelType,
        int Epochs,
        int BatchSize,
        float LearningRate,
        string Optimizer,
        int InputWidth,
        int InputHeight,
        int InputChannels = 3,
        string? DeviceName = "AUTO",
        bool HorizontalFlip = true,
        int EarlyStoppingPatience = 0,
        float EvaluationConfidence = 0.25f,
        float NmsIou = 0.45f,
        int MaxDetections = 100,
        Action<string>? Log = null,
        Action<int, int, string>? Progress = null,
        Action<string>? PreviewGenerated = null);

    public sealed record Result(
        string BestModelPath,
        string MetadataPath,
        double BestMap50,
        double BestMap50_95,
        float FinalTrainLoss,
        IReadOnlyList<string> Classes,
        string DatasetFingerprint,
        double ElapsedMilliseconds);

    public static Result Run(Options options, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options.DatasetRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.OutputRoot);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.Epochs, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.BatchSize, 1);

        options = options with { Optimizer = NormalizeOptimizer(options.Optimizer) };
        var dataset = DetectionFolderDataset.Load(options.DatasetRoot);
        var device = TorchSharpDeviceManager.ResolveDevice(options.DeviceName);
        var inputWidth = NormalizeEdge(options.InputWidth);
        var inputHeight = NormalizeEdge(options.InputHeight);
        var channels = options.InputChannels is 1 or 3 ? options.InputChannels : 3;
        var archName = Yolo26DetectFactory.ResolveArch(options.ModelType);

        Directory.CreateDirectory(options.OutputRoot);
        var bestPath = Path.Combine(options.OutputRoot, BestFileName);
        var bestMetadataPath = bestPath + ".info";
        var lastPath = Path.Combine(options.OutputRoot, LastFileName);
        var lastMetadataPath = lastPath + ".info";

        options.Log?.Invoke(
            $"--- TorchSharp DET training: arch={archName}, classes={dataset.Classes.Count}, " +
            $"train={dataset.Train.Count}, val={dataset.Val.Count}, device={device}, " +
            $"imgsz={inputWidth}x{inputHeight}, channels={channels}, lr={options.LearningRate}, " +
            $"optimizer={options.Optimizer}, epochs={options.Epochs} ---");

        var watch = Stopwatch.StartNew();
        using var model = Yolo26DetectFactory.Create(archName, channels, dataset.Classes.Count);
        model.to(device);

        using optim.Optimizer optimizer = options.Optimizer switch
        {
            "adam" => optim.Adam(model.parameters(), options.LearningRate),
            "adamw" => optim.AdamW(model.parameters(), options.LearningRate),
            "sgd" => optim.SGD(model.parameters(), options.LearningRate),
            _ => throw new ArgumentException("不支持的优化器。", nameof(options))
        };

        var scheduler = torch.optim.lr_scheduler.CosineAnnealingLR(
            optimizer,
            T_max: options.Epochs,
            eta_min: options.LearningRate * 0.01);
        var earlyStopping = new EarlyStoppingTracker(options.EarlyStoppingPatience);

        var bestMap50 = -1d;
        var bestMap5095 = 0d;
        var bestEvaluation = (DetectionEvaluation?)null;
        var finalTrainLoss = float.NaN;
        var completedEpochs = 0;

        for (var epoch = 1; epoch <= options.Epochs; epoch++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            model.train();
            var currentLr = (float)scheduler.get_last_lr().FirstOrDefault(options.LearningRate);
            var lossSum = 0d;
            var batches = 0;

            foreach (var batch in Shuffle(dataset.Train, options.BatchSize, epoch))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (images, truth) = BuildBatch(
                    batch, dataset.Classes.Count, channels, inputWidth, inputHeight,
                    device, options.HorizontalFlip, epoch, cancellationToken);
                using (images)
                using (var scope = NewDisposeScope())
                {
                    optimizer.zero_grad();
                    var predictions = model.forward(images);
                    try
                    {
                        using var loss = DetectionLoss.Compute(predictions, truth, Yolo26DetectModel.BaseStride);
                        loss.backward();
                        optimizer.step();
                        lossSum += loss.item<float>();
                        batches++;
                    }
                    finally
                    {
                        foreach (var prediction in predictions)
                        {
                            prediction.Dispose();
                        }
                    }
                }
            }

            finalTrainLoss = (float)(lossSum / Math.Max(1, batches));
            var evaluation = Evaluate(
                model, dataset, channels, inputWidth, inputHeight, device, options, cancellationToken);
            completedEpochs = epoch;
            var improved = evaluation.Map50 > bestMap50;

            if (improved)
            {
                bestMap50 = evaluation.Map50;
                bestMap5095 = evaluation.Map5095;
                bestEvaluation = evaluation;
                AtomicFileWriter.Write(bestPath, temporary => model.save(temporary));
                WriteSidecar(bestMetadataPath, options, archName, dataset, inputWidth, inputHeight,
                    channels, bestMap50, bestMap5095, finalTrainLoss, evaluation, completedEpochs);
                options.Log?.Invoke(
                    $"TorchSharp DET best epoch {epoch}: mAP50={bestMap50:F4}, mAP50-95={bestMap5095:F4}");
            }

            AtomicFileWriter.Write(lastPath, temporary => model.save(temporary));
            WriteSidecar(lastMetadataPath, options, archName, dataset, inputWidth, inputHeight,
                channels, bestMap50, bestMap5095, finalTrainLoss, bestEvaluation, completedEpochs);

            options.Log?.Invoke(
                "DV_METRICS " + JsonSerializer.Serialize(new Dictionary<string, object?>
                {
                    ["epoch"] = epoch,
                    ["phase"] = "val",
                    ["lr"] = currentLr,
                    ["train_loss"] = finalTrainLoss,
                    ["box_loss"] = finalTrainLoss,
                    ["box_map_50"] = evaluation.Map50,
                    ["box_map_50_95"] = evaluation.Map5095,
                    ["per_class"] = evaluation.PerClass
                        .Select(metric => new Dictionary<string, object?>
                        {
                            ["class"] = metric.ClassName,
                            ["precision"] = metric.Precision,
                            ["recall"] = metric.Recall,
                            ["f1"] = metric.F1,
                            ["support"] = metric.GroundTruths
                        })
                        .ToArray()
                }));
            options.Progress?.Invoke(
                epoch,
                options.Epochs,
                $"TorchSharp DET epoch {epoch}/{options.Epochs} mAP50={evaluation.Map50:F4}");

            if (epoch == 1 || epoch == options.Epochs || epoch % 5 == 0)
            {
                TryWritePreview(model, dataset, channels, inputWidth, inputHeight, device, options, epoch,
                    cancellationToken);
            }

            scheduler.step();
            if (!earlyStopping.Report(evaluation.Map50))
            {
                options.Log?.Invoke(
                    $"TorchSharp DET early stop: {earlyStopping.BadEpochs} epochs without improvement");
                break;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        watch.Stop();
        if (!File.Exists(bestPath))
        {
            // Every epoch writes `last`; an interrupted run still leaves a usable checkpoint.
            AtomicFileWriter.Write(bestPath, temporary => model.save(temporary));
            WriteSidecar(bestMetadataPath, options, archName, dataset, inputWidth, inputHeight,
                channels, bestMap50, bestMap5095, finalTrainLoss, bestEvaluation, completedEpochs);
        }

        options.Log?.Invoke(
            $"TorchSharp DET done: best_mAP50={bestMap50:F4}, best_mAP50-95={bestMap5095:F4}, " +
            $"elapsed={watch.Elapsed.TotalSeconds:F1}s");
        return new Result(
            bestPath,
            bestMetadataPath,
            bestMap50,
            bestMap5095,
            finalTrainLoss,
            dataset.Classes,
            dataset.Fingerprint,
            watch.Elapsed.TotalMilliseconds);
    }

    private static (Tensor Images, List<IReadOnlyList<GroundTruthBox>> Truth) BuildBatch(
        IReadOnlyList<DetectionSample> samples,
        int classCount,
        int channels,
        int width,
        int height,
        torch.Device device,
        bool horizontalFlip,
        int epoch,
        CancellationToken cancellationToken)
    {
        var tensors = new List<Tensor>(samples.Count);
        var truth = new List<IReadOnlyList<GroundTruthBox>>(samples.Count);
        try
        {
            var random = new Random(1000 + epoch);
            foreach (var sample in samples)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var source = TorchSharpImageTensor.LoadMat(sample.ImagePath, channels);
                var letterbox = DetectionLetterbox.For(source.Width, source.Height, width, height);
                var boxes = ReadGroundTruth(sample, classCount, source.Width, source.Height)
                    .Select(box => letterbox.Transform(box))
                    .Where(box => box is not null)
                    .Select(box => box!.Value)
                    .ToList();

                using var canvas = letterbox.Apply(source);
                if (horizontalFlip && random.Next(2) == 0)
                {
                    Cv2.Flip(canvas, canvas, FlipMode.Y);
                    boxes = boxes
                        .Select(box => box with { X = width - box.X - box.Width })
                        .ToList();
                }

                tensors.Add(DetectionLetterbox.ToTensor(canvas, device, PreprocessingVersion.LetterboxV1));
                truth.Add(boxes);
            }

            var images = torch.cat(tensors, 0);
            foreach (var tensor in tensors)
            {
                tensor.Dispose();
            }

            tensors.Clear();
            return (images, truth);
        }
        finally
        {
            foreach (var tensor in tensors)
            {
                tensor.Dispose();
            }
        }
    }

    private static DetectionEvaluation Evaluate(
        Yolo26DetectModel model,
        DetectionDataset dataset,
        int channels,
        int width,
        int height,
        torch.Device device,
        Options options,
        CancellationToken cancellationToken)
    {
        model.eval();
        var images = new List<DetectionImage>(dataset.Val.Count);
        foreach (var sample in dataset.Val)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var source = TorchSharpImageTensor.LoadMat(sample.ImagePath, channels);
            var letterbox = DetectionLetterbox.For(source.Width, source.Height, width, height);
            using var canvas = letterbox.Apply(source);
            using var tensor = DetectionLetterbox.ToTensor(canvas, device, PreprocessingVersion.LetterboxV1);
            var predictions = model.forward(tensor);
            ScoredDetection[] decoded;
            try
            {
                decoded = DetectionTensorDecoder
                    .DecodeAll(predictions, Yolo26DetectModel.BaseStride,
                        options.EvaluationConfidence, options.NmsIou, options.MaxDetections)
                    .ToArray();
            }
            finally
            {
                foreach (var prediction in predictions)
                {
                    prediction.Dispose();
                }
            }

            var mapped = decoded
                .Select(letterbox.ToSource)
                .Where(box => box is not null)
                .Select(box => box!.Value)
                .ToArray();
            images.Add(new DetectionImage(
                ReadGroundTruth(sample, dataset.Classes.Count, source.Width, source.Height),
                mapped));
        }

        return DetectionMetrics.Evaluate(images, dataset.Classes);
    }

    private static IReadOnlyList<GroundTruthBox> ReadGroundTruth(
        DetectionSample sample,
        int classCount,
        int imageWidth,
        int imageHeight)
    {
        if (!File.Exists(sample.LabelPath))
        {
            return Array.Empty<GroundTruthBox>();
        }

        var boxes = YoloDatasetFormat.ParseLabels(
            File.ReadAllText(sample.LabelPath), classCount, out _);
        return boxes
            .Select(box => new GroundTruthBox(
                box.ClassIndex,
                (float)((box.CenterX - (box.Width / 2)) * imageWidth),
                (float)((box.CenterY - (box.Height / 2)) * imageHeight),
                (float)(box.Width * imageWidth),
                (float)(box.Height * imageHeight)))
            .Where(box => box.Width > 0 && box.Height > 0)
            .ToArray();
    }

    private static IReadOnlyList<IReadOnlyList<DetectionSample>> Shuffle(
        IReadOnlyList<DetectionSample> samples,
        int batchSize,
        int epoch)
    {
        var indices = Enumerable.Range(0, samples.Count).ToArray();
        var random = new Random(1000 + epoch);
        for (var index = indices.Length - 1; index > 0; index--)
        {
            var swap = random.Next(index + 1);
            (indices[index], indices[swap]) = (indices[swap], indices[index]);
        }

        var batches = new List<IReadOnlyList<DetectionSample>>();
        for (var start = 0; start < indices.Length; start += batchSize)
        {
            batches.Add(indices
                .Skip(start)
                .Take(batchSize)
                .Select(index => samples[index])
                .ToArray());
        }

        return batches;
    }

    private static void TryWritePreview(
        Yolo26DetectModel model,
        DetectionDataset dataset,
        int channels,
        int width,
        int height,
        torch.Device device,
        Options options,
        int epoch,
        CancellationToken cancellationToken)
    {
        if (options.PreviewGenerated is null || dataset.Val.Count == 0)
        {
            return;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sample = dataset.Val[0];
            using var source = TorchSharpImageTensor.LoadMat(sample.ImagePath, channels);
            var letterbox = DetectionLetterbox.For(source.Width, source.Height, width, height);
            var truth = ReadGroundTruth(sample, dataset.Classes.Count, source.Width, source.Height)
                .Select(box => letterbox.Transform(box))
                .Where(box => box is not null)
                .Select(box => box!.Value)
                .ToArray();

            using var canvas = letterbox.Apply(source);
            using var tensor = DetectionLetterbox.ToTensor(canvas, device, PreprocessingVersion.LetterboxV1);
            var predictions = model.forward(tensor);
            ScoredDetection[] decoded;
            try
            {
                decoded = DetectionTensorDecoder
                    .DecodeAll(predictions, Yolo26DetectModel.BaseStride,
                        options.EvaluationConfidence, options.NmsIou, options.MaxDetections)
                    .ToArray();
            }
            finally
            {
                foreach (var prediction in predictions)
                {
                    prediction.Dispose();
                }
            }

            foreach (var box in truth)
            {
                Cv2.Rectangle(canvas,
                    new Rect((int)box.X, (int)box.Y, (int)box.Width, (int)box.Height),
                    new CvScalar(0, 200, 0), 2);
            }

            foreach (var box in decoded)
            {
                Cv2.Rectangle(canvas,
                    new Rect((int)box.X, (int)box.Y, (int)box.Width, (int)box.Height),
                    new CvScalar(0, 0, 230), 2);
                Cv2.PutText(canvas, $"{box.ClassIndex}:{box.Score:0.00}",
                    new Point((int)box.X, Math.Max(12, (int)box.Y - 4)),
                    HersheyFonts.HersheySimplex, 0.4, new CvScalar(0, 0, 230), 1);
            }

            var previewDirectory = Path.Combine(options.OutputRoot, "previews");
            Directory.CreateDirectory(previewDirectory);
            var path = Path.Combine(previewDirectory, $"epoch_{epoch:D4}.png");
            Cv2.ImWrite(path, canvas);
            options.PreviewGenerated(path);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            options.Log?.Invoke($"预览生成失败：{ex.Message}");
        }
    }

    private static string NormalizeOptimizer(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "adam" => "adam",
            "adamw" => "adamw",
            "sgd" => "sgd",
            _ => throw new ArgumentException($"不支持的优化器：{value}", nameof(value))
        };

    private static int NormalizeEdge(int value)
    {
        var edge = Math.Max(32, value);
        if (edge % 32 != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value), value, "YOLO26 检测输入边长必须是 32 的倍数。");
        }

        return edge;
    }

    private static void WriteSidecar(
        string metadataPath,
        Options options,
        string archName,
        DetectionDataset dataset,
        int inputWidth,
        int inputHeight,
        int channels,
        double bestMap50,
        double bestMap5095,
        float finalTrainLoss,
        DetectionEvaluation? evaluation,
        int completedEpochs)
    {
        var payload = new Dictionary<string, object?>
        {
            ["schema_version"] = 2,
            ["artifact_kind"] = "trained_model",
            ["task"] = "detect",
            ["model_type"] = options.ModelType,
            ["arch"] = archName,
            ["classes"] = dataset.Classes.ToList(),
            ["num_classes"] = dataset.Classes.Count,
            ["max_detections"] = options.MaxDetections,
            ["nms_iou"] = options.NmsIou,
            ["evaluation_confidence"] = options.EvaluationConfidence,
            ["strides"] = new[] { Yolo26DetectModel.BaseStride, Yolo26DetectModel.BaseStride * 2, Yolo26DetectModel.BaseStride * 4 },
            ["dataset_fingerprint"] = dataset.Fingerprint,
            ["epochs"] = completedEpochs,
            ["configured_epochs"] = options.Epochs,
            ["batch_size"] = options.BatchSize,
            ["lr"] = options.LearningRate,
            ["optimizer"] = options.Optimizer,
            ["input_size"] = Math.Max(inputWidth, inputHeight),
            ["input_width"] = inputWidth,
            ["input_height"] = inputHeight,
            ["input_channels"] = channels,
            ["preprocessing"] = PreprocessingVersion.LetterboxV1.ToWireValue(),
            ["weights_format"] = WeightsFormat,
            ["box_map_50"] = bestMap50,
            ["box_map_50_95"] = bestMap5095,
            ["best_map"] = bestMap50,
            ["best_map50"] = bestMap50,
            ["torchsharp"] = new Dictionary<string, object?>
            {
                ["arch"] = archName,
                ["input_width"] = inputWidth,
                ["input_height"] = inputHeight,
                ["num_classes"] = dataset.Classes.Count,
                ["input_channels"] = channels,
                ["stride"] = Yolo26DetectModel.BaseStride
            },
            ["metrics"] = evaluation is null
                ? null
                : new Dictionary<string, object?>
                {
                    ["per_class"] = evaluation.PerClass
                        .Select(metric => new Dictionary<string, object?>
                        {
                            ["class"] = metric.ClassName,
                            ["ground_truths"] = metric.GroundTruths,
                            ["predictions"] = metric.Predictions,
                            ["precision"] = metric.Precision,
                            ["recall"] = metric.Recall,
                            ["f1"] = metric.F1,
                            ["ap50"] = metric.Ap50,
                            ["ap50_95"] = metric.Ap5095
                        })
                        .ToArray()
                },
            ["training_date"] = DateTime.UtcNow.ToString("O"),
            ["notes"] =
                $"TorchSharp {archName} mAP50={bestMap50:F6}, mAP50-95={bestMap5095:F6}, " +
                $"train_loss={finalTrainLoss:F6}"
        };

        var json = JsonSerializer.Serialize(
            payload,
            new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });
        AtomicFileWriter.WriteAllText(metadataPath, json);
    }
}
