using DeepVisionSharp.TorchSharp.Detection.Yolo26;

namespace DeepVisionSharp.TorchSharp.Detection.Training;

/// <summary>One image's ground truth and decoded predictions, both in source pixels.</summary>
public sealed record DetectionImage(
    IReadOnlyList<GroundTruthBox> Truth,
    IReadOnlyList<ScoredDetection> Predictions);

public sealed record DetectionClassMetrics(
    int ClassIndex,
    string ClassName,
    int GroundTruths,
    int Predictions,
    double Precision,
    double Recall,
    double F1,
    double Ap50,
    double Ap5095);

public sealed record DetectionEvaluation(
    double Map50,
    double Map5095,
    IReadOnlyList<DetectionClassMetrics> PerClass);

/// <summary>
/// COCO-style average precision over IoU 0.5:0.95, plus P/R/F1 at confidence 0.5.
/// Classes without ground truth are excluded from mAP but still report their prediction count.
/// </summary>
public static class DetectionMetrics
{
    private static readonly double[] IouThresholds =
        [0.5, 0.55, 0.6, 0.65, 0.7, 0.75, 0.8, 0.85, 0.9, 0.95];

    private const double ReportConfidence = 0.5;

    public static DetectionEvaluation Evaluate(
        IReadOnlyList<DetectionImage> images,
        IReadOnlyList<string> classes)
    {
        var perClass = new List<DetectionClassMetrics>(classes.Count);
        for (var classIndex = 0; classIndex < classes.Count; classIndex++)
        {
            var classIndexLocal = classIndex;
            var groundTruths = images.Sum(image => image.Truth.Count(box => box.ClassIndex == classIndexLocal));
            var predictions = images
                .SelectMany((image, imageIndex) => image.Predictions
                    .Where(box => box.ClassIndex == classIndexLocal)
                    .Select(box => (ImageIndex: imageIndex, Box: box)))
                .OrderByDescending(item => item.Box.Score)
                .ToArray();

            var ap = IouThresholds
                .Select(threshold => AveragePrecision(images, predictions, classIndexLocal, threshold, groundTruths))
                .ToArray();
            var (truePositives, falsePositives) = MatchAt(
                images, predictions, classIndexLocal, iouThreshold: 0.5, ReportConfidence);

            var precision = truePositives + falsePositives == 0
                ? 0
                : (double)truePositives / (truePositives + falsePositives);
            var recall = groundTruths == 0 ? 0 : (double)truePositives / groundTruths;
            var f1 = precision + recall == 0 ? 0 : 2 * precision * recall / (precision + recall);

            perClass.Add(new DetectionClassMetrics(
                classIndex,
                classes[classIndex],
                groundTruths,
                predictions.Length,
                precision,
                recall,
                f1,
                ap[0],
                ap.Average()));
        }

        var evaluated = perClass.Where(item => item.GroundTruths > 0).ToArray();
        return new DetectionEvaluation(
            evaluated.Length == 0 ? 0 : evaluated.Average(item => item.Ap50),
            evaluated.Length == 0 ? 0 : evaluated.Average(item => item.Ap5095),
            perClass);
    }

    private static double AveragePrecision(
        IReadOnlyList<DetectionImage> images,
        IReadOnlyList<(int ImageIndex, ScoredDetection Box)> predictions,
        int classIndex,
        double iouThreshold,
        int groundTruths)
    {
        if (groundTruths == 0 || predictions.Count == 0)
        {
            return 0;
        }

        var matched = new HashSet<(int ImageIndex, int BoxIndex)>();
        var points = new List<(double Recall, double Precision)>();
        var truePositives = 0;
        var falsePositives = 0;
        foreach (var prediction in predictions)
        {
            var match = FindBestMatch(images, prediction, classIndex, iouThreshold, matched);
            if (match is int boxIndex)
            {
                matched.Add((prediction.ImageIndex, boxIndex));
                truePositives++;
            }
            else
            {
                falsePositives++;
            }

            points.Add((
                (double)truePositives / groundTruths,
                (double)truePositives / (truePositives + falsePositives)));
        }

        // Make precision monotonically non-increasing, then integrate over recall.
        var best = 0d;
        for (var index = points.Count - 1; index >= 0; index--)
        {
            best = Math.Max(best, points[index].Precision);
            points[index] = (points[index].Recall, best);
        }

        var average = 0d;
        var previousRecall = 0d;
        foreach (var (recall, precision) in points)
        {
            if (recall > previousRecall)
            {
                average += (recall - previousRecall) * precision;
                previousRecall = recall;
            }
        }

        return average;
    }

    private static (int TruePositives, int FalsePositives) MatchAt(
        IReadOnlyList<DetectionImage> images,
        IReadOnlyList<(int ImageIndex, ScoredDetection Box)> predictions,
        int classIndex,
        double iouThreshold,
        double confidence)
    {
        var matched = new HashSet<(int ImageIndex, int BoxIndex)>();
        var truePositives = 0;
        var falsePositives = 0;
        foreach (var prediction in predictions.Where(item => item.Box.Score >= confidence))
        {
            var match = FindBestMatch(images, prediction, classIndex, iouThreshold, matched);
            if (match is int boxIndex)
            {
                matched.Add((prediction.ImageIndex, boxIndex));
                truePositives++;
            }
            else
            {
                falsePositives++;
            }
        }

        return (truePositives, falsePositives);
    }

    private static int? FindBestMatch(
        IReadOnlyList<DetectionImage> images,
        (int ImageIndex, ScoredDetection Box) prediction,
        int classIndex,
        double iouThreshold,
        HashSet<(int ImageIndex, int BoxIndex)> matched)
    {
        var truth = images[prediction.ImageIndex].Truth;
        var bestIndex = -1;
        var bestIoU = iouThreshold;
        for (var index = 0; index < truth.Count; index++)
        {
            var candidate = truth[index];
            if (candidate.ClassIndex != classIndex || matched.Contains((prediction.ImageIndex, index)))
            {
                continue;
            }

            var iou = IoU(candidate, prediction.Box);
            if (iou >= bestIoU)
            {
                bestIoU = iou;
                bestIndex = index;
            }
        }

        return bestIndex >= 0 ? bestIndex : null;
    }

    private static double IoU(GroundTruthBox truth, ScoredDetection prediction)
    {
        var left = Math.Max(truth.X, prediction.X);
        var top = Math.Max(truth.Y, prediction.Y);
        var right = Math.Min(truth.X + truth.Width, prediction.X + prediction.Width);
        var bottom = Math.Min(truth.Y + truth.Height, prediction.Y + prediction.Height);
        var intersection = Math.Max(0, right - left) * Math.Max(0, bottom - top);
        if (intersection <= 0)
        {
            return 0;
        }

        var union = (truth.Width * truth.Height) + (prediction.Width * prediction.Height) - intersection;
        return union <= 0 ? 0 : intersection / union;
    }
}
