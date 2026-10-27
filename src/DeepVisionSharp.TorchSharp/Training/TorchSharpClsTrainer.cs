using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenCvSharp;
using TorchSharp;
using DeepVisionSharp.TorchSharp.Classification;
using DeepVisionSharp.TorchSharp.Device;
using DeepVisionSharp.TorchSharp.IO;
using DeepVisionSharp.TorchSharp.Classification.Pretrained;
using DeepVisionSharp.TorchSharp.Models;
using DeepVisionSharp.TorchSharp.Tensors;
using DeepVisionSharp.Domain.ValueObjects;
using static TorchSharp.torch;
using static TorchSharp.torch.nn;
using TorchDevice = TorchSharp.torch.Device;
using CvScalar = OpenCvSharp.Scalar;
using CvSize = OpenCvSharp.Size;

namespace DeepVisionSharp.TorchSharp.Training;

public static class TorchSharpClsTrainer
{
    public const string BestFileName = "best.torchsharp";
    public const string WeightsFormat = "torchsharp";
    private static string NormalizeOptimizer(string value) => value?.Trim().ToLowerInvariant() switch
    {
        "sgd" => "sgd",
        "adam" => "adam",
        "adamw" => "adamw",
        _ => throw new ArgumentException($"不支持的优化器：{value}", nameof(value))
    };

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
        ClassificationAugmentation? ClassificationAugmentation = null,
        PreprocessingVersion Preprocessing = PreprocessingVersion.UnitScaleV1,
        string? PretrainedDirectory = null,
        int EarlyStoppingPatience = 0,
        Action<string>? Log = null,
        Action<int, int, string>? Progress = null);

    public sealed record Result(
        string BestModelPath,
        string MetadataPath,
        float BestValAccuracy,
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

        var dataset = ClassificationFolderDataset.Load(options.DatasetRoot);
        var device = TorchSharpDeviceManager.ResolveDevice(options.DeviceName);
        var inputWidth = Math.Max(8, options.InputWidth);
        var inputHeight = Math.Max(8, options.InputHeight);
        var imageSize = Math.Max(inputWidth, inputHeight);
        var channels = options.InputChannels is 1 or 3 ? options.InputChannels : 3;

        Directory.CreateDirectory(options.OutputRoot);
        var bestPath = Path.Combine(options.OutputRoot, BestFileName);
        var metadataPath = bestPath + ".info";

        options.Log?.Invoke(
            $"TorchSharp CLS 训练开始：train={dataset.TrainSamples.Count}, val={dataset.ValSamples.Count}, " +
            $"classes={dataset.Classes.Count}, device={device}, imgsz={imageSize}, channels={channels}, lr={options.LearningRate}");

        var watch = Stopwatch.StartNew();
        var archName = TorchSharpClassifierFactory.ResolveArch(options.ModelType);
        using var model = TorchSharpClassifierFactory.Create(archName, channels, dataset.Classes.Count, imageSize);
        model.to(device);
        TryApplyPretrainedWeights(model, options, archName);

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
        float bestValAcc = -1f;
        float finalTrainLoss = float.NaN;
        Evaluation? bestEvaluation = null;
        // Early stopping means the configured epoch count is not what actually ran; the sidecar
        // records the real number so the UI does not claim a 12-epoch run trained for 50.
        var completedEpochs = 0;

        for (var epoch = 1; epoch <= options.Epochs; epoch++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            model.train();

            var currentLr = (float)scheduler.get_last_lr().FirstOrDefault(options.LearningRate);
            var trainLossSum = 0.0;
            var trainBatches = 0;
            foreach (var (images, labels) in EnumerateBatches(
                         dataset.TrainSamples, options.BatchSize, channels, inputWidth, inputHeight, device, shuffle: true, augment: true, seed: 1000 + epoch, augmentation: options.ClassificationAugmentation, preprocessing: options.Preprocessing))
            {
                cancellationToken.ThrowIfCancellationRequested();
                using (images)
                using (labels)
                using (var scope = NewDisposeScope())
                {
                    optimizer.zero_grad();
                    using var logits = model.forward(images);
                    using var loss = functional.cross_entropy(logits, labels);
                    loss.backward();
                    optimizer.step();
                    trainLossSum += loss.item<float>();
                    trainBatches++;
                }
            }

            finalTrainLoss = (float)(trainLossSum / Math.Max(1, trainBatches));
            // Evaluating on the training set would make val_acc meaningless and select the best
            // checkpoint by memorisation. An empty validation split is a dataset problem, so say so
            // instead of silently producing an optimistic metric.
            if (dataset.ValSamples.Count == 0)
            {
                throw new InvalidOperationException(
                    "数据集没有验证样本，无法计算验证指标；请检查导出的 train/val 划分。");
            }

            var evaluation = Evaluate(
                model,
                dataset.ValSamples,
                options.BatchSize,
                channels,
                inputWidth,
                inputHeight,
                device,
                dataset.Classes.Count,
                options.Preprocessing,
                cancellationToken);

            var perClass = evaluation.PerClass(dataset.Classes);
            options.Log?.Invoke(
                $"epoch {epoch}/{options.Epochs}: lr={currentLr:E3}, train_loss={finalTrainLoss:F4}, val_loss={evaluation.Loss:F4}, val_acc={evaluation.Top1:F4}, val_top5={evaluation.Top5:F4}");
            options.Log?.Invoke(
                "per-class: " + string.Join(
                    ", ",
                    perClass.Select(metric =>
                        $"{metric.ClassName} P={metric.Precision:F3} R={metric.Recall:F3} F1={metric.F1:F3} (n={metric.Support})")));

            options.Log?.Invoke(
                "DV_METRICS " + JsonSerializer.Serialize(new Dictionary<string, object?>
                {
                    ["epoch"] = epoch,
                    ["phase"] = "val",
                    ["lr"] = currentLr,
                    ["train_loss"] = finalTrainLoss,
                    ["val_loss"] = evaluation.Loss,
                    ["accuracy_top1"] = evaluation.Top1,
                    ["accuracy_top5"] = evaluation.Top5,
                    ["per_class"] = perClass
                        .Select(metric => new Dictionary<string, object?>
                        {
                            ["class"] = metric.ClassName,
                            ["precision"] = metric.Precision,
                            ["recall"] = metric.Recall,
                            ["f1"] = metric.F1,
                            ["support"] = metric.Support
                        })
                        .ToArray(),
                    ["confusion"] = evaluation.ConfusionMatrix()
                }));
            options.Progress?.Invoke(epoch, options.Epochs, $"TorchSharp CLS epoch {epoch}/{options.Epochs}");

            // Previews cost nine forward passes plus a JPEG encode. Once every five epochs (and on
            // the first/last) is enough for the run panel and keeps the loop focused on training.
            if (epoch == 1 || epoch == options.Epochs || epoch % 5 == 0)
            {
                try
                {
                    GeneratePreviews(
                        model,
                        dataset,
                        options.OutputRoot,
                        inputWidth,
                        inputHeight,
                        device,
                        options.Preprocessing,
                        cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    options.Log?.Invoke($"生成验证预览图异常: {ex.Message}");
                }
            }

            if (evaluation.Top1 >= bestValAcc)
            {
                bestValAcc = evaluation.Top1;
                bestEvaluation = evaluation;
                // Weights first, then the sidecar. A crash in between leaves new weights with the
                // previous best's sidecar; within one run arch/classes/input size are identical, so
                // the pair still loads and only the recorded metrics lag one improvement behind.
                AtomicFileWriter.Write(bestPath, tempPath => model.save(tempPath));
                WriteSidecar(
                    metadataPath,
                    options,
                    archName,
                    dataset,
                    imageSize,
                    channels,
                    bestValAcc,
                    finalTrainLoss,
                    bestEvaluation,
                    completedEpochs);
            }

            if (earlyStopping.IsEnabled)
            {
                earlyStopping.Report(evaluation.Top1);
                if (earlyStopping.ShouldStop)
                {
                    options.Log?.Invoke(
                        $"early stopping: 连续 {earlyStopping.BadEpochs} 轮无提升（best={earlyStopping.Best:F4}），在第 {epoch} 轮停止");
                    break;
                }
            }

            scheduler.step();
            completedEpochs = epoch;
        }

        // The epoch loop only checks the token at its top, so a stop pressed during the last epoch's
        // evaluation or checkpoint write would otherwise run to completion and be recorded as
        // Succeeded. Re-check before saving so the job reports Cancelled.
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(bestPath) || !File.Exists(metadataPath))
        {
            AtomicFileWriter.Write(bestPath, tempPath => model.save(tempPath));
            WriteSidecar(
                metadataPath,
                options,
                archName,
                dataset,
                imageSize,
                channels,
                Math.Max(0f, bestValAcc),
                finalTrainLoss,
                bestEvaluation,
                completedEpochs);
        }

        watch.Stop();
        options.Log?.Invoke($"TorchSharp CLS 训练完成：best_acc={bestValAcc:F4}, path={bestPath}");

        return new Result(
            bestPath,
            metadataPath,
            Math.Max(0f, bestValAcc),
            finalTrainLoss,
            dataset.Classes,
            dataset.Fingerprint,
            watch.Elapsed.TotalMilliseconds);
    }

    private sealed record Evaluation(float Loss, float Top1, float Top5, int[] Confusion, int ClassCount)
    {
        /// <summary>Row = ground truth, column = prediction.</summary>
        public int[][] ConfusionMatrix()
        {
            var matrix = new int[ClassCount][];
            for (var row = 0; row < ClassCount; row++)
            {
                matrix[row] = new int[ClassCount];
                Array.Copy(Confusion, row * ClassCount, matrix[row], 0, ClassCount);
            }

            return matrix;
        }

        /// <summary>
        /// Per-class precision/recall/F1. A binary OK/NG problem where the model predicts the
        /// majority class for everything shows up here as recall 0 on the other class, which global
        /// accuracy hides.
        /// </summary>
        public IReadOnlyList<PerClassMetric> PerClass(IReadOnlyList<string> classNames)
        {
            var metrics = new List<PerClassMetric>(ClassCount);
            for (var classIndex = 0; classIndex < ClassCount; classIndex++)
            {
                var truePositive = Confusion[(classIndex * ClassCount) + classIndex];
                var support = 0;
                var predicted = 0;
                for (var other = 0; other < ClassCount; other++)
                {
                    support += Confusion[(classIndex * ClassCount) + other];
                    predicted += Confusion[(other * ClassCount) + classIndex];
                }

                var falsePositive = predicted - truePositive;
                var falseNegative = support - truePositive;
                var precision = predicted > 0 ? truePositive / (double)predicted : 0;
                var recall = support > 0 ? truePositive / (double)support : 0;
                var f1 = precision + recall > 0 ? 2 * precision * recall / (precision + recall) : 0;
                var name = classIndex < classNames.Count ? classNames[classIndex] : $"class_{classIndex}";
                metrics.Add(new PerClassMetric(name, precision, recall, f1, support, falsePositive, falseNegative));
            }

            return metrics;
        }
    }

    internal sealed record PerClassMetric(
        string ClassName,
        double Precision,
        double Recall,
        double F1,
        int Support,
        int FalsePositive,
        int FalseNegative);

    private static Evaluation Evaluate(
        Module<Tensor, Tensor> model,
        IReadOnlyList<ClassificationFolderDataset.Sample> samples,
        int batchSize,
        int channels,
        int inputWidth,
        int inputHeight,
        TorchDevice device,
        int classCount,
        PreprocessingVersion preprocessing,
        CancellationToken cancellationToken)
    {
        model.eval();
        var lossSum = 0.0;
        var correct = 0;
        var top5Correct = 0;
        var total = 0;
        var batches = 0;
        var confusion = new int[classCount * classCount];

        using (no_grad())
        {
            foreach (var (images, labels) in EnumerateBatches(
                         samples, batchSize, channels, inputWidth, inputHeight, device, shuffle: false, augment: false, seed: 0, augmentation: null, preprocessing: preprocessing))
            {
                cancellationToken.ThrowIfCancellationRequested();
                using (images)
                using (labels)
                using (var scope = NewDisposeScope())
                {
                    using var logits = model.forward(images);
                    using var loss = functional.cross_entropy(logits, labels);
                    lossSum += loss.item<float>();
                    batches++;
                    using var preds = logits.argmax(1);
                    correct += (preds == labels).sum().ToInt32();

                    using var predCpu = preds.cpu();
                    using var labelCpu = labels.cpu();
                    var predictedClasses = predCpu.data<long>().ToArray();
                    var trueClasses = labelCpu.data<long>().ToArray();
                    for (var index = 0; index < trueClasses.Length; index++)
                    {
                        var truth = (int)trueClasses[index];
                        var prediction = (int)predictedClasses[index];
                        if ((uint)truth < (uint)classCount && (uint)prediction < (uint)classCount)
                        {
                            confusion[(truth * classCount) + prediction]++;
                        }
                    }

                    using var topK = logits.topk(Math.Min(5, (int)logits.shape[1]), dim: 1).indexes;
                    using var expandedLabels = labels.unsqueeze(1);
                    top5Correct += topK.eq(expandedLabels).any(1).sum().ToInt32();
                    total += (int)labels.shape[0];
                }
            }
        }

        return new Evaluation(
            (float)(lossSum / Math.Max(1, batches)),
            total == 0 ? 0f : correct / (float)total,
            total == 0 ? 0f : top5Correct / (float)total,
            confusion,
            classCount);
    }

    private static IEnumerable<(Tensor Images, Tensor Labels)> EnumerateBatches(
        IReadOnlyList<ClassificationFolderDataset.Sample> samples,
        int batchSize,
        int channels,
        int inputWidth,
        int inputHeight,
        TorchDevice device,
        bool shuffle,
        bool augment,
        int seed,
        ClassificationAugmentation? augmentation,
        PreprocessingVersion preprocessing)
    {
        if (samples.Count == 0)
        {
            yield break;
        }

        var order = Enumerable.Range(0, samples.Count).ToArray();
        if (shuffle)
        {
            var rng = new Random(seed);
            for (var shuffleIndex = order.Length - 1; shuffleIndex > 0; shuffleIndex--)
            {
                var swapIndex = rng.Next(shuffleIndex + 1);
                (order[shuffleIndex], order[swapIndex]) = (order[swapIndex], order[shuffleIndex]);
            }
        }

        var augmentRng = augment ? new Random(seed + 777) : null;
        var normalizedAugmentation = ClassificationAugmentation.Normalize(augmentation);

        for (var offset = 0; offset < order.Length; offset += batchSize)
        {
            var batchCount = Math.Min(batchSize, order.Length - offset);
            var tensors = new List<Tensor>(batchCount);
            var labels = new long[batchCount];
            Tensor? images = null;
            Tensor? labelTensor = null;
            try
            {
                for (var index = 0; index < batchCount; index++)
                {
                    var sample = samples[order[offset + index]];
                    labels[index] = sample.ClassIndex;
                    using var mat = TorchSharpImageTensor.LoadMat(sample.ImagePath, channels);
                    var horizontalFlip = augment && augmentRng!.NextDouble() < normalizedAugmentation.FlipLeftRight;
                    var verticalFlip = augment && augmentRng!.NextDouble() < normalizedAugmentation.FlipUpDown;

                    tensors.Add(TorchSharpImageTensor.ToClassificationTensor(
                        mat,
                        inputWidth,
                        inputHeight,
                        device,
                        enableHorizontalFlip: horizontalFlip,
                        enableVerticalFlip: verticalFlip,
                        preprocessing: preprocessing));
                }

                images = cat(tensors.ToArray(), dim: 0);
                // Allocate on the target device directly; `.to(device)` would leave the CPU source
                // tensor undisposed on every batch.
                labelTensor = tensor(labels, [batchCount], ScalarType.Int64, device);
            }
            finally
            {
                foreach (var tensorItem in tensors)
                {
                    tensorItem.Dispose();
                }
            }

            yield return (images!, labelTensor!);
        }
    }

    private static void GeneratePreviews(
        Module<Tensor, Tensor> model,
        ClassificationFolderDataset dataset,
        string outputRoot,
        int inputWidth,
        int inputHeight,
        TorchDevice device,
        PreprocessingVersion preprocessing,
        CancellationToken cancellationToken)
    {
        var previewDir = Path.Combine(outputRoot, "preview");
        Directory.CreateDirectory(previewDir);

        // Preview images come from the validation split when there is one; falling back to the
        // training split would show the operator images the model was fitted on.
        var candidateSamples = dataset.ValSamples;
        if (candidateSamples.Count == 0)
        {
            return;
        }

        var samplesToDisplay = candidateSamples.Take(9).ToList();
        const int cellWidth = 320;
        const int cellHeight = 320;
        const int headerHeight = 36;
        const int cols = 3;
        var rows = (samplesToDisplay.Count + cols - 1) / cols;

        var canvasWidth = cols * cellWidth;
        var canvasHeight = rows * (cellHeight + headerHeight);

        using var canvas = new Mat(canvasHeight, canvasWidth, MatType.CV_8UC3, new CvScalar(30, 30, 35));

        model.eval();
        using (no_grad())
        {
            for (var sampleDisplayIndex = 0; sampleDisplayIndex < samplesToDisplay.Count; sampleDisplayIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var sample = samplesToDisplay[sampleDisplayIndex];
                var columnGridIndex = sampleDisplayIndex % cols;
                var rowGridIndex = sampleDisplayIndex / cols;

                var cellPositionX = columnGridIndex * cellWidth;
                var cellPositionY = rowGridIndex * (cellHeight + headerHeight);

                Mat? rawMat = null;
                try
                {
                    rawMat = TorchSharpImageTensor.LoadMat(sample.ImagePath, 3);
                }
                catch
                {
                }

                if (rawMat is null || rawMat.Empty())
                {
                    continue;
                }

                using (rawMat)
                using (var resizedMat = new Mat())
                {
                    Cv2.Resize(rawMat, resizedMat, new CvSize(cellWidth, cellHeight));

                    using var inputTensor = TorchSharpImageTensor.ToClassificationTensor(rawMat, inputWidth, inputHeight, device, preprocessing: preprocessing);
                    using var logits = model.forward(inputTensor);
                    using var probabilities = functional.softmax(logits, dim: 1);
                    var predIndex = (int)probabilities.argmax(1).item<long>();
                    var confidence = probabilities[0, predIndex].item<float>();

                    var isCorrect = predIndex == sample.ClassIndex;
                    var groundTruthName = sample.ClassIndex < dataset.Classes.Count ? dataset.Classes[sample.ClassIndex] : $"C{sample.ClassIndex}";
                    var predictedName = predIndex < dataset.Classes.Count ? dataset.Classes[predIndex] : $"C{predIndex}";

                    var imageRegion = new Rect(cellPositionX, cellPositionY + headerHeight, cellWidth, cellHeight);
                    using var subImage = new Mat(canvas, imageRegion);
                    resizedMat.CopyTo(subImage);

                    var headerRegion = new Rect(cellPositionX, cellPositionY, cellWidth, headerHeight);
                    var headerColor = isCorrect ? new CvScalar(35, 120, 35) : new CvScalar(35, 35, 180);
                    Cv2.Rectangle(canvas, headerRegion, headerColor, -1);

                    var labelText = $"GT: {groundTruthName} | Pred: {predictedName} ({confidence:P0})";
                    Cv2.PutText(
                        canvas,
                        labelText,
                        new Point(cellPositionX + 8, cellPositionY + 24),
                        HersheyFonts.HersheySimplex,
                        0.52,
                        CvScalar.All(255),
                        1,
                        LineTypes.AntiAlias);

                    Cv2.Rectangle(canvas, new Rect(cellPositionX, cellPositionY, cellWidth, cellHeight + headerHeight), new CvScalar(60, 60, 65), 1);
                }
            }
        }

        if (Cv2.ImEncode(".jpg", canvas, out var jpgBytes))
        {
            var previewPath = Path.Combine(previewDir, "preview_0.jpg");
            File.WriteAllBytes(previewPath, jpgBytes);
        }
    }

    private static void TryApplyPretrainedWeights(
        Module<Tensor, Tensor> model,
        Options options,
        string archName)
    {
        if (string.IsNullOrWhiteSpace(options.PretrainedDirectory)
            || !Directory.Exists(options.PretrainedDirectory))
        {
            options.Log?.Invoke($"pretrained: 未找到预训练权重（{archName}），使用随机初始化");
            return;
        }

        var widths = TorchSharpClassifierFactory.ResolveStageWidths(archName);
        if (widths is null)
        {
            options.Log?.Invoke($"pretrained: 架构 {archName} 不支持预训练权重，使用随机初始化");
            return;
        }

        try
        {
            using var bundle = PretrainedWeightsBundle.Load(options.PretrainedDirectory);
            var result = PretrainedInitializer.Apply(model, bundle, widths);
            options.Log?.Invoke(
                $"pretrained: arch={archName} loaded={result.Loaded} sliced={result.Sliced} missing={result.Missing} unexpected={result.Unexpected} preprocessing={options.Preprocessing.ToWireValue()}");
        }
        catch (Exception ex)
        {
            // Weights are an optimisation: a broken bundle must not block training.
            options.Log?.Invoke($"pretrained: 加载失败，回退随机初始化：{ex.Message}");
        }
    }
    private static void WriteSidecar(
        string metadataPath,
        Options options,
        string archName,
        ClassificationFolderDataset dataset,
        int imageSize,
        int channels,
        float bestValAcc,
        float finalTrainLoss,
        Evaluation? bestEvaluation,
        int completedEpochs)
    {
        var payload = new Dictionary<string, object?>
        {
            ["schema_version"] = 2,
            ["artifact_kind"] = "trained_model",
            ["model_type"] = options.ModelType,
            ["classes"] = dataset.Classes.ToList(),
            ["num_classes"] = dataset.Classes.Count,
            ["max_detections"] = 1,
            ["dataset_fingerprint"] = dataset.Fingerprint,
            ["epochs"] = completedEpochs,
            ["configured_epochs"] = options.Epochs,
            ["batch_size"] = options.BatchSize,
            ["lr"] = options.LearningRate,
            ["optimizer"] = options.Optimizer,
            ["input_size"] = imageSize,
            ["input_width"] = options.InputWidth > 0 ? options.InputWidth : imageSize,
            ["input_height"] = options.InputHeight > 0 ? options.InputHeight : imageSize,
            ["input_channels"] = channels,
            ["best_map"] = bestValAcc,
            ["best_map50"] = bestValAcc,
            ["weights_format"] = WeightsFormat,
            ["preprocessing"] = options.Preprocessing.ToWireValue(),
            ["arch"] = archName,
            ["torchsharp"] = new Dictionary<string, object?>
            {
                ["arch"] = archName,
                ["image_size"] = imageSize,
                ["num_classes"] = dataset.Classes.Count,
                ["input_channels"] = channels
            },
            // Per-class metrics of the best epoch. Readers that only know schema 2 ignore this block.
            ["metrics"] = bestEvaluation is null
                ? null
                : new Dictionary<string, object?>
                {
                    ["confusion"] = bestEvaluation.ConfusionMatrix(),
                    ["per_class"] = bestEvaluation.PerClass(dataset.Classes)
                        .Select(metric => new Dictionary<string, object?>
                        {
                            ["class"] = metric.ClassName,
                            ["precision"] = metric.Precision,
                            ["recall"] = metric.Recall,
                            ["f1"] = metric.F1,
                            ["support"] = metric.Support,
                            ["false_positive"] = metric.FalsePositive,
                            ["false_negative"] = metric.FalseNegative
                        })
                        .ToArray()
                },
            ["training_date"] = DateTime.UtcNow.ToString("O"),
            ["notes"] =
                $"TorchSharp {archName} val_acc={bestValAcc:F6}, train_loss={finalTrainLoss:F6}"
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
