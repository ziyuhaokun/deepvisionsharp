using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

using OpenCvSharp;
using TorchSharp;
using static TorchSharp.torch;

using DeepVisionSharp.Domain.Datasets;
using DeepVisionSharp.TorchSharp.Classification.Pretrained;
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

    /// <summary>
    /// Validation decodes far below the operational confidence: COCO-style AP needs low-score true
    /// positives in the recall curve. NMS and MaxDetections still bound the candidate list.
    /// </summary>
    private const float EvaluationScoreFloor = 0.001f;

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
        string? PretrainedDirectory = null,
        int EarlyStoppingPatience = 0,
        float EvaluationConfidence = 0.25f,
        float NmsIou = 0.45f,
        int MaxDetections = 100,
        Action<string>? Log = null,
        Action<int, int, string>? Progress = null,
        Action<string>? PreviewGenerated = null,
        Guid? TrainingJobId = null);

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
        if (options.TrainingJobId is not null && (File.Exists(bestPath) || File.Exists(lastPath)))
        {
            throw new IOException("检测训练输出目录已有 checkpoint，请为新任务使用独立目录。");
        }

        options.Log?.Invoke(
            $"--- TorchSharp DET training: arch={archName}, classes={dataset.Classes.Count}, " +
            $"train={dataset.Train.Count}, val={dataset.Val.Count}, device={device}, " +
            $"imgsz={inputWidth}x{inputHeight}, channels={channels}, lr={options.LearningRate}, " +
            $"optimizer={options.Optimizer}, epochs={options.Epochs} ---");

        var watch = Stopwatch.StartNew();
        var initialization = InitializeModel(options, archName, channels, dataset.Classes.Count);
        using var model = initialization.Model;
        cancellationToken.ThrowIfCancellationRequested();
        const int baseStride = Yolo26DetectModel.BaseStride;
        var run = new DetectionTrainingRun(
            options.TrainingJobId, options.ModelType, archName, dataset.Classes.ToArray(), inputWidth, inputHeight,
            channels, dataset.Fingerprint, PreprocessingVersions.LetterboxWire, [baseStride, baseStride * 2, baseStride * 4],
            model.UseSppfShortcut, options.NmsIou, options.MaxDetections, options.EvaluationConfidence, EvaluationScoreFloor,
            initialization.BundleName, initialization.BundleArch, initialization.LoadedTensors, options.Epochs,
            options.BatchSize, options.LearningRate, options.Optimizer);
        run.Write(options.OutputRoot);
        model.to(device);

        var iterationsPerEpoch =
            Math.Max(1, (int)Math.Ceiling(dataset.Train.Count / (double)options.BatchSize));
        var totalIterations = iterationsPerEpoch * options.Epochs;
        var warmupIterations = TrainingOptimization.WarmupIterations(iterationsPerEpoch, options.Epochs);
        var parameterGroups = TrainingOptimization.GroupParameters(model);
        using var trainingOptimizer = TrainingOptimizer.Create(
            options.Optimizer, parameterGroups, options.LearningRate);
        var optimizer = trainingOptimizer.Optimizer;
        options.Log?.Invoke(
            $"训练调度：optimizer={options.Optimizer}, total_iterations={totalIterations}, " +
            $"warmup_iterations={warmupIterations}, weight_decay={TrainingOptimization.WeightDecay}, " +
            $"groups(decay/norm/bias)={parameterGroups.DecayWeights.Count}/" +
            $"{parameterGroups.NormWeights.Count}/{parameterGroups.Biases.Count}");
        options.Log?.Invoke(
            $"mAP 评估解码分数下限={EvaluationScoreFloor}，预览仍用 {options.EvaluationConfidence}");

        var earlyStopping = new EarlyStoppingTracker(options.EarlyStoppingPatience);
        // Epochs the warmup owns are not evidence of a plateau, so they cannot trigger early stop.
        var warmupEpochs = warmupIterations / iterationsPerEpoch;
        var bestEpochTracker = new DetectionBestEpochTracker();

        var bestMap50 = -1d;
        var bestMap5095 = 0d;
        var bestEvaluation = (DetectionEvaluation?)null;
        var finalTrainLoss = float.NaN;
        var finalBoxLoss = float.NaN;
        var finalClsLoss = float.NaN;
        var completedEpochs = 0;
        var iteration = 0;

        for (var epoch = 1; epoch <= options.Epochs; epoch++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            model.train();
            // One RNG per epoch drives both the shuffle and the flips, so a batch's flip pattern is
            // no longer a copy of every other batch's (the previous per-batch seed did that).
            var epochRandom = new Random(1000 + epoch);
            var lossSum = 0d;
            var boxLossSum = 0d;
            var clsLossSum = 0d;
            var batches = 0;

            foreach (var batch in Shuffle(dataset.Train, options.BatchSize, epochRandom))
            {
                cancellationToken.ThrowIfCancellationRequested();
                trainingOptimizer.ApplySchedule(iteration, warmupIterations, totalIterations);
                var (images, truth) = BuildBatch(
                    batch, dataset.Classes.Count, channels, inputWidth, inputHeight,
                    device, options.HorizontalFlip, epochRandom, cancellationToken);
                using (images)
                using (var scope = NewDisposeScope())
                {
                    optimizer.zero_grad();
                    var predictions = model.forward(images);
                    try
                    {
                        var parts = DetectionLoss.ComputeDetailed(predictions, truth, Yolo26DetectModel.BaseStride);
                        using var loss = parts.Total;
                        loss.backward();
                        optimizer.step();
                        lossSum += loss.item<float>();
                        boxLossSum += parts.Box;
                        clsLossSum += parts.Cls;
                        batches++;
                        iteration++;
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

            var batchCount = Math.Max(1, batches);
            finalTrainLoss = (float)(lossSum / batchCount);
            finalBoxLoss = (float)(boxLossSum / batchCount);
            finalClsLoss = (float)(clsLossSum / batchCount);
            var evaluation = Evaluate(
                model, dataset, channels, inputWidth, inputHeight, device, options, cancellationToken);
            completedEpochs = epoch;
            var currentLr = (float)trainingOptimizer.LastLearningRate;
            var improved = bestEpochTracker.Report(epoch, evaluation.Map50, evaluation.Map5095);

            if (improved)
            {
                bestMap50 = evaluation.Map50;
                bestMap5095 = evaluation.Map5095;
                bestEvaluation = evaluation;
                AtomicFileWriter.Write(bestPath, temporary => model.save(temporary));
                WriteSidecar(bestMetadataPath, run, bestMap50, bestMap5095, finalTrainLoss, evaluation, completedEpochs);
                options.Log?.Invoke(
                    $"TorchSharp DET best epoch {epoch}: mAP50={bestMap50:F4}, " +
                    $"mAP50-95={bestMap5095:F4}, fitness={bestEpochTracker.BestFitness:F4}");
            }

            AtomicFileWriter.Write(lastPath, temporary => model.save(temporary));
            WriteSidecar(lastMetadataPath, run, evaluation.Map50, evaluation.Map5095, finalTrainLoss, evaluation, completedEpochs);

            options.Log?.Invoke(
                "DV_METRICS " + JsonSerializer.Serialize(new Dictionary<string, object?>
                {
                    ["epoch"] = epoch,
                    ["phase"] = "val",
                    ["lr"] = currentLr,
                    ["train_loss"] = finalTrainLoss,
                    ["box_loss"] = finalBoxLoss,
                    ["cls_loss"] = finalClsLoss,
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

            earlyStopping.Report(bestEpochTracker.BestFitness, withinWarmup: epoch <= warmupEpochs);
            var shouldStopEarly = earlyStopping.ShouldStop;
            if (epoch == 1 || epoch == options.Epochs || epoch % 5 == 0 || shouldStopEarly)
            {
                TryWritePreview(model, dataset, channels, inputWidth, inputHeight, device, options, epoch,
                    cancellationToken);
            }

            if (shouldStopEarly)
            {
                options.Log?.Invoke(
                    $"TorchSharp DET early stop: {earlyStopping.BadEpochs} epochs without improvement");
                break;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        watch.Stop();
        if (!File.Exists(bestPath) || !File.Exists(bestMetadataPath))
        {
            // Every epoch writes `last`; an interrupted run still leaves a usable checkpoint.
            bestMap50 = Math.Max(0d, bestMap50);
            bestMap5095 = Math.Max(0d, bestMap5095);
            AtomicFileWriter.Write(bestPath, temporary => model.save(temporary));
            WriteSidecar(bestMetadataPath, run, bestMap50, bestMap5095, finalTrainLoss, bestEvaluation, completedEpochs);
        }

        options.Log?.Invoke(
            $"TorchSharp DET done: best_mAP50={bestMap50:F4}, best_mAP50-95={bestMap5095:F4}, " +
            $"best_epoch={bestEpochTracker.BestEpoch}, elapsed={watch.Elapsed.TotalSeconds:F1}s");
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

    private static (Yolo26DetectModel Model, string? BundleName, string? BundleArch, int LoadedTensors) InitializeModel(
        Options options, string archName, int channels, int classCount)
    {
        var model = Yolo26DetectFactory.Create(archName, channels, classCount);
        var directory = options.PretrainedDirectory;
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            options.Log?.Invoke($"pretrained: 未找到预训练权重（{archName}），使用随机初始化");
            return (model, null, null, 0);
        }

        try
        {
            using var bundle = PretrainedWeightsBundle.Load(directory);
            if (string.IsNullOrWhiteSpace(bundle.Arch))
            {
                throw new InvalidDataException("权重包缺少架构标识");
            }

            var result = Yolo26PretrainedInitializer.ApplyToDetection(model, bundle);
            if (result.Loaded == 0)
            {
                throw new InvalidDataException("权重包没有匹配的张量");
            }

            options.Log?.Invoke(
                $"pretrained: arch={archName} bundle={bundle.Arch} loaded={result.Loaded} " +
                $"missing={result.Missing} unexpected={result.Unexpected}");
            return (model, Path.GetFileName(Path.TrimEndingDirectorySeparator(directory)), bundle.Arch, result.Loaded);
        }
        catch (Exception ex)
        {
            // Applying a bundle can fail after an earlier tensor has already been copied.
            model.Dispose();
            options.Log?.Invoke($"pretrained: 加载失败，回退随机初始化：{ex.Message}");
            return (Yolo26DetectFactory.Create(archName, channels, classCount), null, null, 0);
        }
    }

    private static (Tensor Images, List<IReadOnlyList<GroundTruthBox>> Truth) BuildBatch(
        IReadOnlyList<DetectionSample> samples,
        int classCount,
        int channels,
        int width,
        int height,
        torch.Device device,
        bool horizontalFlip,
        Random random,
        CancellationToken cancellationToken)
    {
        var tensors = new List<Tensor>(samples.Count);
        var truth = new List<IReadOnlyList<GroundTruthBox>>(samples.Count);
        try
        {
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
        // Validation reads no gradients: without this every epoch keeps the whole graph alive.
        using var noGrad = torch.no_grad();
        var images = new List<DetectionImage>(dataset.Val.Count);
        var samples = new List<DetectionSample>(dataset.Val.Count);
        var truth = new List<IReadOnlyList<GroundTruthBox>>(dataset.Val.Count);
        var letterboxes = new List<DetectionLetterbox>(dataset.Val.Count);
        var inputs = new List<Tensor>(dataset.Val.Count);
        try
        {
            foreach (var sample in dataset.Val)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var source = TorchSharpImageTensor.LoadMat(sample.ImagePath, channels);
                var letterbox = DetectionLetterbox.For(source.Width, source.Height, width, height);
                using var canvas = letterbox.Apply(source);
                inputs.Add(DetectionLetterbox.ToTensor(canvas, device, PreprocessingVersion.LetterboxV1));
                samples.Add(sample);
                letterboxes.Add(letterbox);
                truth.Add(ReadGroundTruth(sample, dataset.Classes.Count, source.Width, source.Height));
            }

            if (inputs.Count == 0)
            {
                return DetectionMetrics.Evaluate(images, dataset.Classes);
            }

            // One forward for the whole validation set; decoding stays per image because each
            // letterbox maps boxes back with its own scale.
            using var batch = torch.cat(inputs, dim: 0);
            var predictions = model.forward(batch);
            try
            {
                for (var index = 0; index < samples.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using var scope = NewDisposeScope();
                    var perImage = predictions
                        .Select(prediction => prediction[index].unsqueeze(0))
                        .ToArray();
                    var decoded = DetectionTensorDecoder.DecodeAll(
                            perImage,
                            Yolo26DetectModel.BaseStride,
                            EvaluationScoreFloor,
                            options.NmsIou,
                            options.MaxDetections)
                        .ToArray();
                    var mapped = decoded
                        .Select(letterboxes[index].ToSource)
                        .Where(box => box is not null)
                        .Select(box => box!.Value)
                        .ToArray();
                    images.Add(new DetectionImage(truth[index], mapped));
                }
            }
            finally
            {
                foreach (var prediction in predictions)
                {
                    prediction.Dispose();
                }
            }
        }
        finally
        {
            foreach (var input in inputs)
            {
                input.Dispose();
            }
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
        Random random)
    {
        var indices = Enumerable.Range(0, samples.Count).ToArray();
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
            using var noGrad = torch.no_grad();
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
            if (Cv2.ImEncode(".png", canvas, out var bytes))
            {
                AtomicFileWriter.Write(path, temporary => File.WriteAllBytes(temporary, bytes));
                options.PreviewGenerated(path);
            }
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
        DetectionTrainingRun run,
        double bestMap50,
        double bestMap5095,
        float finalTrainLoss,
        DetectionEvaluation? evaluation,
        int completedEpochs)
    {
        var payload = run.CreateCheckpointMetadata();
        payload["epochs"] = completedEpochs;
        payload["box_map_50"] = bestMap50;
        payload["box_map_50_95"] = bestMap5095;
        payload["best_map"] = bestMap50;
        payload["best_map50"] = bestMap50;
        payload["fitness"] = TrainingOptimization.DetectionFitness(bestMap50, bestMap5095);
        payload["metrics"] = evaluation is null
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
            };
        payload["training_date"] = DateTime.UtcNow.ToString("O");
        payload["notes"] = $"TorchSharp {run.Arch} mAP50={bestMap50:F6}, mAP50-95={bestMap5095:F6}, " +
            $"train_loss={finalTrainLoss:F6}";

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
