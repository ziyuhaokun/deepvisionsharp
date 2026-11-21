using TorchSharp;
using TorchSharp.Modules;
using Xunit;
using DeepVisionSharp.TorchSharp.Detection;
using DeepVisionSharp.TorchSharp.Detection.Yolo26;
using static TorchSharp.torch;

namespace DeepVisionSharp.TorchSharp.Tests;

/// <summary>
/// Checks the task-aligned assignment and the CIoU / BCE terms against the decode contract that
/// inference reads, without going through the network.
/// </summary>
public sealed class DetectionLossTests
{
    /// <summary>
    /// The box channels below rebuild the same box from every anchor, so every candidate overlaps
    /// the truth perfectly and an assigned anchor is the only place the class target is non-zero.
    /// Its class gradient is then negative while background anchors keep a positive one, which makes
    /// the assignment visible without exposing the assigner.
    /// </summary>
    [Fact]
    public void Assignment_keeps_positive_anchors_inside_the_box_and_within_topk()
    {
        (int Height, int Width)[] grids = [(12, 12), (6, 6), (3, 3)];
        var truth = new GroundTruthBox(0, X: 32f, Y: 32f, Width: 32f, Height: 32f);
        using var fields = BuildExactFields(grids, baseStride: 8, numClasses: 1, truth);

        var batch = new List<IReadOnlyList<GroundTruthBox>> { new[] { truth } };
        using (var loss = DetectionLoss.Compute(fields.Predictions, batch, strides: 8))
        {
            loss.backward();
        }

        var positivesInAllScales = 0;
        for (var scale = 0; scale < grids.Length; scale++)
        {
            var stride = 8 << scale;
            var gradient = fields.ClassFields[scale].grad!;
            var positives = 0;
            for (var cellY = 0; cellY < grids[scale].Height; cellY++)
            {
                for (var cellX = 0; cellX < grids[scale].Width; cellX++)
                {
                    if (gradient[0, 0, cellY, cellX].item<float>() >= 0)
                    {
                        continue;
                    }

                    positives++;
                    var centreX = (cellX + 0.5f) * stride;
                    var centreY = (cellY + 0.5f) * stride;
                    Assert.True(
                        centreX > truth.X && centreX < truth.X + truth.Width &&
                        centreY > truth.Y && centreY < truth.Y + truth.Height,
                        $"stride {stride} 的第 ({cellX},{cellY}) 格被分配，但中心 ({centreX},{centreY}) 不在框内。");
                }
            }

            positivesInAllScales += positives;
        }

        // Top-k is applied once per ground truth across every scale, not once per scale.
        Assert.InRange(positivesInAllScales, 1, 10);
    }

    [Fact]
    public void Box_smaller_than_a_cell_still_gets_one_positive_anchor()
    {
        (int Height, int Width)[] grids = [(12, 12)];
        var truth = new GroundTruthBox(0, X: 50f, Y: 50f, Width: 3f, Height: 3f);
        using var fields = BuildExactFields(grids, baseStride: 8, numClasses: 1, truth);

        var batch = new List<IReadOnlyList<GroundTruthBox>> { new[] { truth } };
        using (var loss = DetectionLoss.Compute(fields.Predictions, batch, strides: 8))
        {
            loss.backward();
        }

        var gradient = fields.ClassFields[0].grad!;
        var centres = new List<(int X, int Y)>();
        for (var cellY = 0; cellY < grids[0].Height; cellY++)
        {
            for (var cellX = 0; cellX < grids[0].Width; cellX++)
            {
                if (gradient[0, 0, cellY, cellX].item<float>() < 0)
                {
                    centres.Add((cellX, cellY));
                }
            }
        }

        // No cell centre lies inside a 3 px box, so the nearest centre must be the single positive.
        var centre = Assert.Single(centres);
        Assert.Equal((6, 6), centre);
    }

    [Fact]
    public void Batch_without_ground_truth_has_no_box_loss_and_a_finite_class_loss()
    {
        using var boxField = full([1, 4, 8, 8], 1.0f);
        using var classField = zeros([1, 2, 8, 8]);
        using var prediction = cat([boxField, classField], dim: 1);

        var emptyImage = new List<IReadOnlyList<GroundTruthBox>> { Array.Empty<GroundTruthBox>() };
        var parts = DetectionLoss.ComputeDetailed([prediction], emptyImage, strides: 8);
        using (parts.Total)
        {
            Assert.Equal(0f, parts.Box);
            Assert.True(float.IsFinite(parts.Cls), $"cls 损失不是有限值：{parts.Cls}。");
            Assert.True(parts.Cls > 0f, "没有目标时 cls 损失仍应把分数压下去。");
            Assert.Equal(parts.Box + parts.Cls, parts.Total.item<float>(), 3);
            Assert.True(float.IsFinite(parts.Total.item<float>()), "总损失不是有限值。");
        }

        // An empty batch list must behave the same, not throw on the missing image entries.
        var partsWithoutBatch = DetectionLoss.ComputeDetailed([prediction], [], strides: 8);
        using (partsWithoutBatch.Total)
        {
            Assert.Equal(0f, partsWithoutBatch.Box);
            Assert.True(float.IsFinite(partsWithoutBatch.Cls), "空批次列表的 cls 损失不是有限值。");
        }
    }

    [Fact]
    public void Optimising_the_raw_fields_learns_a_box_the_decoder_reads_back()
    {
        const int gridSize = 16;
        const int stride = 8;
        var truth = new GroundTruthBox(0, X: 40f, Y: 40f, Width: 40f, Height: 40f);

        using var boxData = full([1, 4, gridSize, gridSize], 1.0f);
        // Ultralytics class prior for one class at stride 8: log(5 / 1 / (640 / 8)^2).
        using var classData = full([1, 1, gridSize, gridSize], -7.15f);
        using var boxField = new Parameter(boxData);
        using var classField = new Parameter(classData);
        using var optimizer = torch.optim.Adam([boxField, classField], lr: 0.05);

        var batch = new List<IReadOnlyList<GroundTruthBox>> { new[] { truth } };
        var first = 0f;
        var last = 0f;
        for (var step = 0; step < 400; step++)
        {
            using var prediction = cat([boxField, classField], dim: 1);
            using var loss = DetectionLoss.Compute([prediction], batch, stride);
            last = loss.item<float>();
            if (step == 0)
            {
                first = last;
            }

            optimizer.zero_grad();
            loss.backward();
            optimizer.step();
        }

        Assert.True(last < first * 0.5f, $"损失只从 {first:F4} 降到 {last:F4}，没有收敛。");

        using var trained = cat([boxField, classField], dim: 1);
        var detections = DetectionTensorDecoder.DecodeAll(
            [trained],
            baseStride: stride,
            scoreThreshold: 0.25f,
            iouThreshold: 0.5f,
            maxDetections: 10);
        Assert.NotEmpty(detections);
        var best = detections.Max(detection => IntersectionOverUnion(detection, truth));
        Assert.True(best > 0.8f, $"解码出的最优框与真值 IoU 只有 {best:F3}。");
    }

    private static float IntersectionOverUnion(ScoredDetection detection, GroundTruthBox truth)
    {
        var x0 = Math.Max(detection.X, truth.X);
        var y0 = Math.Max(detection.Y, truth.Y);
        var x1 = Math.Min(detection.X + detection.Width, truth.X + truth.Width);
        var y1 = Math.Min(detection.Y + detection.Height, truth.Y + truth.Height);
        var intersection = Math.Max(0f, x1 - x0) * Math.Max(0f, y1 - y0);
        var union = (detection.Width * detection.Height) + (truth.Width * truth.Height) - intersection;
        return union > 0f ? intersection / union : 0f;
    }

    /// <summary>
    /// Builds one leaf field per scale whose box channels reproduce <paramref name="box"/> exactly
    /// from every cell centre, plus a zero class logit field.
    /// </summary>
    private static Fields BuildExactFields(
        (int Height, int Width)[] grids,
        int baseStride,
        int numClasses,
        GroundTruthBox box)
    {
        var predictions = new List<Tensor>();
        var boxFields = new List<Tensor>();
        var classFields = new List<Tensor>();
        for (var scale = 0; scale < grids.Length; scale++)
        {
            var (height, width) = grids[scale];
            var stride = baseStride << scale;
            var cells = height * width;
            var values = new float[4 * cells];
            for (var cellY = 0; cellY < height; cellY++)
            {
                for (var cellX = 0; cellX < width; cellX++)
                {
                    var centreX = (cellX + 0.5f) * stride;
                    var centreY = (cellY + 0.5f) * stride;
                    var offset = (cellY * width) + cellX;
                    values[offset] = (centreX - box.X) / stride;
                    values[cells + offset] = (centreY - box.Y) / stride;
                    values[(2 * cells) + offset] = ((box.X + box.Width) - centreX) / stride;
                    values[(3 * cells) + offset] = ((box.Y + box.Height) - centreY) / stride;
                }
            }

            var boxField = tensor(values, [1, 4, height, width]).requires_grad_(true);
            var classField = zeros([1, numClasses, height, width]).requires_grad_(true);
            predictions.Add(cat([boxField, classField], dim: 1));
            boxFields.Add(boxField);
            classFields.Add(classField);
        }

        return new Fields(predictions, boxFields, classFields);
    }

    private sealed record Fields(List<Tensor> Predictions, List<Tensor> BoxFields, List<Tensor> ClassFields) : IDisposable
    {
        public void Dispose()
        {
            foreach (var tensor in Predictions.Concat(BoxFields).Concat(ClassFields))
            {
                tensor.Dispose();
            }
        }
    }
}