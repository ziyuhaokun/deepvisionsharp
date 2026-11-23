using TorchSharp;
using static TorchSharp.torch;

namespace DeepVisionSharp.TorchSharp.Detection.Yolo26;

/// <summary>
/// One ground-truth box: class index and the rectangle in pixels, top-left origin.
/// </summary>
public readonly record struct GroundTruthBox(int ClassIndex, float X, float Y, float Width, float Height);

/// <summary>Total detection loss plus its two weighted terms, so a trainer can log them apart.</summary>
/// <remarks>
/// <see cref="Box"/> and <see cref="Cls"/> already carry the loss gains, so <c>Total</c> is their sum.
/// The caller owns <see cref="Total"/>.
/// </remarks>
public readonly record struct DetectionLossParts(Tensor Total, float Box, float Cls);

/// <summary>
/// Task-aligned detection loss: an anchor may only be positive when its centre sits inside a
/// ground-truth box, and out of those candidates the anchors that already predict the box best win.
/// </summary>
/// <remarks>
/// The tensors keep the inference contract, one (batch, 4 + classes, height, width) tensor per scale
/// with raw left/top/right/bottom distances in stride units from the cell centre, so a predicted box
/// is <c>centre -/+ distance * stride</c> in pixels. Assignment reads no gradients: the alignment
/// score and every target come from detached tensors, and only the two loss terms differ.
/// Ground truths are padded to the widest image in the batch and gated by a presence mask.
/// </remarks>
public static class DetectionLoss
{
    /// <summary>Weight of the CIoU term, matching the Ultralytics default.</summary>
    private const float BoxGain = 7.5f;

    /// <summary>Weight of the classification term, matching the Ultralytics default.</summary>
    private const float ClassGain = 0.5f;

    private const int CandidatesPerBox = 10;
    private const double AlignAlpha = 0.5;
    private const double AlignBeta = 6.0;
    private const double AssignEps = 1e-9;
    private const double BoxEps = 1e-7;

    public static Tensor Compute(
        IReadOnlyList<Tensor> predictions,
        IReadOnlyList<IReadOnlyList<GroundTruthBox>> batch,
        int strides) => ComputeDetailed(predictions, batch, strides).Total;

    /// <summary>
    /// Runs the task-aligned assignment, then the CIoU and BCE-with-logits losses, and reports the
    /// weighted scalars alongside the differentiable total.
    /// </summary>
    public static DetectionLossParts ComputeDetailed(
        IReadOnlyList<Tensor> predictions,
        IReadOnlyList<IReadOnlyList<GroundTruthBox>> batch,
        int strides)
    {
        using var scope = torch.NewDisposeScope();
        var device = predictions[0].device;
        var batchSize = (int)predictions[0].shape[0];
        var numClasses = (int)predictions[0].shape[1] - 4;

        // One anchor per cell centre, flattened in row-major cell order so a scale view and its
        // anchors line up. The anchor stride is the pixel size of one distance unit at that scale.
        var flatPredictions = new List<Tensor>(predictions.Count);
        var anchorPoints = new List<Tensor>(predictions.Count);
        var anchorStrides = new List<Tensor>(predictions.Count);
        for (var scale = 0; scale < predictions.Count; scale++)
        {
            var prediction = predictions[scale];
            var height = (int)prediction.shape[2];
            var width = (int)prediction.shape[3];
            var stride = strides << scale;
            var columns = arange(width, dtype: ScalarType.Float32, device: device).add(0.5f).unsqueeze(0);
            var rows = arange(height, dtype: ScalarType.Float32, device: device).add(0.5f).unsqueeze(1);
            var anchor = stack(
                [
                    columns.expand(height, width).mul(stride).reshape(-1),
                    rows.expand(height, width).mul(stride).reshape(-1)
                ],
                dim: 1);
            anchorPoints.Add(anchor.MoveToOuterDisposeScope());
            anchorStrides.Add(
                full(height * width, stride, dtype: ScalarType.Float32, device: device).MoveToOuterDisposeScope());
            flatPredictions.Add(prediction
                .permute(0, 2, 3, 1)
                .reshape(batchSize, height * width, 4 + numClasses)
                .MoveToOuterDisposeScope());
        }

        using var predictionAll = cat(flatPredictions, dim: 1);
        using var anchorAll = cat(anchorPoints, dim: 0);
        using var strideAll = cat(anchorStrides, dim: 0);
        using var predictedBoxes = ToPixelBoxes(predictionAll, anchorAll, strideAll);

        var boxesPerImage = Math.Max(1, batch.Count == 0 ? 0 : batch.Max(item => item.Count));
        var groundTruth = new float[batchSize * boxesPerImage * 4];
        var labels = new long[batchSize * boxesPerImage];
        var present = new float[batchSize * boxesPerImage];
        for (var image = 0; image < batchSize; image++)
        {
            var boxes = image < batch.Count ? batch[image] : Array.Empty<GroundTruthBox>();
            for (var index = 0; index < boxes.Count && index < boxesPerImage; index++)
            {
                var box = boxes[index];
                var offset = ((image * boxesPerImage) + index) * 4;
                groundTruth[offset] = box.X;
                groundTruth[offset + 1] = box.Y;
                groundTruth[offset + 2] = box.X + box.Width;
                groundTruth[offset + 3] = box.Y + box.Height;
                labels[(image * boxesPerImage) + index] = box.ClassIndex;
                present[(image * boxesPerImage) + index] = 1f;
            }
        }

        using var groundTruthAll = tensor(groundTruth, new long[] { batchSize, boxesPerImage, 4 }, device: device);
        using var labelsAll = tensor(
            labels,
            new long[] { batchSize, boxesPerImage },
            dtype: ScalarType.Int64,
            device: device);
        using var presentAll = tensor(present, new long[] { batchSize, boxesPerImage }, device: device);

        var targetBoxes = new List<Tensor>(batchSize);
        var targetScores = new List<Tensor>(batchSize);
        for (var image = 0; image < batchSize; image++)
        {
            using var imageScope = torch.NewDisposeScope();
            using var imageScores = predictionAll[image][.., 4..].sigmoid();
            var (assignedBoxes, assignedScores) = AssignImage(
                predictedBoxes[image].detach(),
                imageScores.detach(),
                groundTruthAll[image],
                labelsAll[image],
                presentAll[image],
                anchorAll,
                numClasses);
            targetBoxes.Add(assignedBoxes.MoveToOuterDisposeScope());
            targetScores.Add(assignedScores.MoveToOuterDisposeScope());
        }

        using var targetBoxesAll = stack(targetBoxes, dim: 0);
        using var targetScoresAll = stack(targetScores, dim: 0);
        using var weights = targetScoresAll.sum(dim: -1);
        using var weightSum = weights.sum().clamp_min(1f);
        using var boxError = CompleteIoU(predictedBoxes, targetBoxesAll).neg().add(1f);
        using var boxLoss = weights.mul(boxError).sum().div(weightSum).mul(BoxGain);
        using var classSum = torch.nn.functional.binary_cross_entropy_with_logits(
            predictionAll[.., .., 4..],
            targetScoresAll,
            reduction: torch.nn.Reduction.Sum);
        using var classLoss = classSum.div(weightSum).mul(ClassGain);
        var total = boxLoss.add(classLoss);
        return new DetectionLossParts(
            total.MoveToOuterDisposeScope(),
            boxLoss.item<float>(),
            classLoss.item<float>());
    }

    /// <summary>
    /// Assigns one image's anchors to its boxes and returns the per-anchor box and class targets.
    /// </summary>
    private static (Tensor Boxes, Tensor Scores) AssignImage(
        Tensor predictedBoxes,
        Tensor classScores,
        Tensor groundTruth,
        Tensor labels,
        Tensor present,
        Tensor anchors,
        int numClasses)
    {
        var device = predictedBoxes.device;
        var numBoxes = (int)groundTruth.shape[0];
        var numAnchors = (int)predictedBoxes.shape[0];

        var centreX = anchors[.., 0].unsqueeze(0);
        var centreY = anchors[.., 1].unsqueeze(0);
        var left = groundTruth[.., 0].unsqueeze(1);
        var top = groundTruth[.., 1].unsqueeze(1);
        var right = groundTruth[.., 2].unsqueeze(1);
        var bottom = groundTruth[.., 3].unsqueeze(1);

        // A candidate is an anchor whose centre is strictly inside the box.
        var inside = minimum(
            minimum(centreX.sub(left), right.sub(centreX)),
            minimum(centreY.sub(top), bottom.sub(centreY)));
        var candidates = inside.gt((float)AssignEps).to(ScalarType.Float32).mul(present.unsqueeze(1));

        // A box smaller than one cell contains no centre, so it takes the nearest anchor instead;
        // without that a tiny defect would never see a positive and stay unlearned.
        var hasCandidate = candidates.sum(dim: 1);
        var boxCentreX = groundTruth[.., 0].add(groundTruth[.., 2]).mul(0.5f).unsqueeze(1);
        var boxCentreY = groundTruth[.., 1].add(groundTruth[.., 3]).mul(0.5f).unsqueeze(1);
        var distance = centreX.sub(boxCentreX).pow(2f).add(centreY.sub(boxCentreY).pow(2f));
        var nearest = distance.argmin(dim: 1).unsqueeze(1);
        var missing = hasCandidate.lt(0.5f).to(ScalarType.Float32).mul(present).unsqueeze(1);
        var fallback = zeros([numBoxes, numAnchors], device: device).scatter_(1, nearest, missing);
        candidates = candidates.maximum(fallback);

        var boxScores = classScores.index_select(1, labels).transpose(0, 1);
        var overlaps = PairwiseIoU(groundTruth, predictedBoxes);
        var align = boxScores.pow(AlignAlpha).mul(overlaps.pow(AlignBeta)).mul(candidates);

        // Keep each box's best anchors, then let a contested anchor go to the box it overlaps most.
        var candidatesPerBox = Math.Min(CandidatesPerBox, numAnchors);
        var (topkMetrics, topkIndices) = align.topk(candidatesPerBox, dim: 1);
        // Invalid top-k slots are redirected to anchor 0, so they must contribute zero.
        // Otherwise a box whose best anchor really is 0 collects every invalid slot and loses
        // its positive entirely (a tiny box near the origin hits exactly that case).
        using var valid = topkMetrics.gt((float)AssignEps);
        using var safeIndices = topkIndices.masked_fill(valid.logical_not(), 0);
        using var contributions = ones([numBoxes, candidatesPerBox], device: device)
            .mul(valid.to(ScalarType.Float32));
        var claimed = zeros([numBoxes, numAnchors], device: device)
            .scatter_add_(1, safeIndices, contributions);
        claimed.masked_fill_(claimed.gt(1f), 0f);
        claimed = claimed.mul(candidates);

        var assignIndex = where(
                claimed.gt(0f),
                overlaps,
                full([numBoxes, numAnchors], -1f, device: device))
            .argmax(dim: 0);
        var ordinals = arange(numBoxes, dtype: ScalarType.Int64, device: device).unsqueeze(1).expand(numBoxes, numAnchors);
        var owned = claimed.mul(ordinals.eq(assignIndex.unsqueeze(0)).to(ScalarType.Float32));

        // Each box's best anchor becomes its target score, scaled down by that box's best overlap.
        var bestAlign = owned.mul(align).amax([1], true).add((float)AssignEps);
        var bestOverlap = owned.mul(overlaps).amax([1], true);
        var normalised = align.mul(owned).mul(bestOverlap).div(bestAlign);
        var targetWeight = normalised.amax([0]);
        var assigned = owned.sum(dim: 0).clamp_max(1f);
        var targetLabel = labels.index_select(0, assignIndex).unsqueeze(1);
        var classTarget = zeros([numAnchors, numClasses], device: device)
            .scatter_(1, targetLabel, assigned.unsqueeze(1));
        var scoreTarget = classTarget.mul(targetWeight.unsqueeze(1));
        var boxTarget = owned.transpose(0, 1).matmul(groundTruth);
        return (boxTarget, scoreTarget);
    }

    /// <summary>Turns the raw distances of every anchor into a box in pixels.</summary>
    private static Tensor ToPixelBoxes(Tensor prediction, Tensor anchors, Tensor anchorStrides)
    {
        var centreX = anchors[.., 0];
        var centreY = anchors[.., 1];
        var left = prediction[.., .., 0].mul(anchorStrides);
        var top = prediction[.., .., 1].mul(anchorStrides);
        var right = prediction[.., .., 2].mul(anchorStrides);
        var bottom = prediction[.., .., 3].mul(anchorStrides);
        return stack(
            [centreX.sub(left), centreY.sub(top), centreX.add(right), centreY.add(bottom)],
            dim: 2);
    }

    /// <summary>IoU between every first box and every second box, as a pair matrix.</summary>
    private static Tensor PairwiseIoU(Tensor first, Tensor second)
    {
        var firstX1 = first[.., 0].unsqueeze(1);
        var firstY1 = first[.., 1].unsqueeze(1);
        var firstX2 = first[.., 2].unsqueeze(1);
        var firstY2 = first[.., 3].unsqueeze(1);
        var secondX1 = second[.., 0].unsqueeze(0);
        var secondY1 = second[.., 1].unsqueeze(0);
        var secondX2 = second[.., 2].unsqueeze(0);
        var secondY2 = second[.., 3].unsqueeze(0);

        var intersection = minimum(firstX2, secondX2)
            .sub(maximum(firstX1, secondX1))
            .clamp_min(0f)
            .mul(minimum(firstY2, secondY2).sub(maximum(firstY1, secondY1)).clamp_min(0f));
        var union = firstX2.sub(firstX1)
            .clamp_min(0f)
            .mul(firstY2.sub(firstY1).clamp_min(0f))
            .add(secondX2.sub(secondX1).clamp_min(0f).mul(secondY2.sub(secondY1).clamp_min(0f)))
            .sub(intersection)
            .clamp_min((float)BoxEps);
        return intersection.div(union).clamp(0f, 1f);
    }

    /// <summary>Complete IoU of aligned pixel boxes, clamped so a degenerate prediction stays finite.</summary>
    private static Tensor CompleteIoU(Tensor predicted, Tensor target)
    {
        var predictedX1 = predicted[.., .., 0];
        var predictedY1 = predicted[.., .., 1];
        var predictedX2 = predicted[.., .., 2];
        var predictedY2 = predicted[.., .., 3];
        var targetX1 = target[.., .., 0];
        var targetY1 = target[.., .., 1];
        var targetX2 = target[.., .., 2];
        var targetY2 = target[.., .., 3];

        // Raw distances may be negative early in training; widths are clamped before any division.
        var predictedWidth = predictedX2.sub(predictedX1).clamp_min((float)BoxEps);
        var predictedHeight = predictedY2.sub(predictedY1).clamp_min((float)BoxEps);
        var targetWidth = targetX2.sub(targetX1).clamp_min((float)BoxEps);
        var targetHeight = targetY2.sub(targetY1).clamp_min((float)BoxEps);

        var intersection = minimum(predictedX2, targetX2)
            .sub(maximum(predictedX1, targetX1))
            .clamp_min(0f)
            .mul(minimum(predictedY2, targetY2).sub(maximum(predictedY1, targetY1)).clamp_min(0f));
        var union = predictedWidth.mul(predictedHeight)
            .add(targetWidth.mul(targetHeight))
            .sub(intersection)
            .add((float)BoxEps);
        var iou = intersection.div(union);

        var spanX = maximum(predictedX2, targetX2).sub(minimum(predictedX1, targetX1));
        var spanY = maximum(predictedY2, targetY2).sub(minimum(predictedY1, targetY1));
        var span = spanX.mul(spanX).add(spanY.mul(spanY)).add((float)BoxEps);
        var centreDistance = targetX1.add(targetX2)
            .sub(predictedX1)
            .sub(predictedX2)
            .pow(2f)
            .add(targetY1.add(targetY2).sub(predictedY1).sub(predictedY2).pow(2f))
            .mul(0.25f);
        var shapeCost = targetWidth.div(targetHeight)
            .arctan()
            .sub(predictedWidth.div(predictedHeight).arctan())
            .pow(2f)
            .mul((float)(4.0 / (Math.PI * Math.PI)));
        var tradeoff = shapeCost.div(shapeCost.sub(iou).add((float)(1 + BoxEps)));
        return iou.sub(centreDistance.div(span).add(shapeCost.mul(tradeoff))).clamp(-1f, 1f);
    }
}

/// <summary>Turns raw per-cell predictions into boxes in image pixels.</summary>
public static class DetectionDecoder
{
    public readonly record struct Detection(float X, float Y, float Width, float Height, int ClassIndex, float Score);

    public static IReadOnlyList<Detection> Decode(
        Tensor prediction,
        int stride,
        float scoreThreshold)
    {
        using var scores = prediction[.., 4..].sigmoid();
        var height = (int)prediction.shape[2];
        var width = (int)prediction.shape[3];
        var numClasses = (int)scores.shape[1];
        var found = new List<Detection>();

        for (var cellY = 0; cellY < height; cellY++)
        {
            for (var cellX = 0; cellX < width; cellX++)
            {
                var bestClass = 0;
                var bestScore = scores[0, 0, cellY, cellX].item<float>();
                for (var classIndex = 1; classIndex < numClasses; classIndex++)
                {
                    var score = scores[0, classIndex, cellY, cellX].item<float>();
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestClass = classIndex;
                    }
                }

                if (bestScore < scoreThreshold)
                {
                    continue;
                }

                var centreX = (cellX + 0.5f) * stride;
                var centreY = (cellY + 0.5f) * stride;
                var left = prediction[0, 0, cellY, cellX].item<float>() * stride;
                var top = prediction[0, 1, cellY, cellX].item<float>() * stride;
                var right = prediction[0, 2, cellY, cellX].item<float>() * stride;
                var bottom = prediction[0, 3, cellY, cellX].item<float>() * stride;
                found.Add(new Detection(
                    centreX - left,
                    centreY - top,
                    left + right,
                    top + bottom,
                    bestClass,
                    bestScore));
            }
        }

        return found;
    }
}