using TorchSharp;
using static TorchSharp.torch;

namespace DeepVisionSharp.TorchSharp.Detection.Yolo26;

/// <summary>
/// One ground-truth box: class index and the rectangle in pixels, top-left origin.
/// </summary>
public readonly record struct GroundTruthBox(int ClassIndex, float X, float Y, float Width, float Height);

/// <summary>
/// Assigns each ground-truth box to the one grid cell whose centre it falls in, then sums a box loss
/// and a classification loss over every scale.
/// </summary>
/// <remarks>
/// The box target is the left/top/right/bottom distance from that cell's centre, in cells. The box
/// loss is the mean absolute error over the assigned cells. The classification loss is a sigmoid
/// focal-free binary cross entropy over every cell, so empty cells learn to predict nothing.
/// </remarks>
public static class DetectionLoss
{
    public static Tensor Compute(
        IReadOnlyList<Tensor> predictions,
        IReadOnlyList<IReadOnlyList<GroundTruthBox>> batch,
        int strides)
    {
        using var scope = torch.NewDisposeScope();
        var device = predictions[0].device;
        var total = zeros(1, device: device);
        for (var scale = 0; scale < predictions.Count; scale++)
        {
            var stride = strides << scale;
            total += ScaleLoss(predictions[scale], batch, stride);
        }

        return total.MoveToOuterDisposeScope();
    }

    private static Tensor ScaleLoss(
        Tensor prediction,
        IReadOnlyList<IReadOnlyList<GroundTruthBox>> batch,
        int stride)
    {
        var batchSize = (int)prediction.shape[0];
        var channels = (int)prediction.shape[1];
        var height = (int)prediction.shape[2];
        var width = (int)prediction.shape[3];
        var numClasses = channels - 4;

        // Class targets start at zero, so a cell with no box assigned must predict no class.
        // Written through index tensors: TorchSharp's multi-index setter misreads the tensor rank.
        var boxTarget = zeros([batchSize, 4, height, width], device: prediction.device);
        var classTarget = zeros([batchSize, numClasses, height, width], device: prediction.device);
        var assigned = zeros([batchSize, 1, height, width], device: prediction.device);

        var images = new List<long>();
        var rows = new List<long>();
        var columns = new List<long>();
        var classes = new List<long>();
        var distances = new List<float>();
        for (var image = 0; image < batchSize; image++)
        {
            foreach (var box in batch[image])
            {
                var cellX = (int)((box.X + (box.Width / 2f)) / stride);
                var cellY = (int)((box.Y + (box.Height / 2f)) / stride);
                if (cellX < 0 || cellX >= width || cellY < 0 || cellY >= height)
                {
                    continue;
                }

                var centreX = cellX + 0.5f;
                var centreY = cellY + 0.5f;
                images.Add(image);
                rows.Add(cellY);
                columns.Add(cellX);
                classes.Add(box.ClassIndex);
                distances.AddRange(
                [
                    centreX - (box.X / stride),
                    centreY - (box.Y / stride),
                    ((box.X + box.Width) / stride) - centreX,
                    ((box.Y + box.Height) / stride) - centreY
                ]);
            }
        }

        if (images.Count > 0)
        {
            var count = images.Count;
            var device = prediction.device;
            using var imageIndex = tensor(images, device: device);
            using var rowIndex = tensor(rows, device: device);
            using var columnIndex = tensor(columns, device: device);
            using var classIndex = tensor(classes, device: device);
            using var distance = tensor(distances, [count, 4], device: device);
            using var sideIndex = arange(4, device: device);
            for (var side = 0; side < 4; side++)
            {
                using var sideColumn = tensor(new[] { (long)side }, device: device).expand(count);
                boxTarget.index_put_(distance[.., side], [imageIndex, sideColumn, rowIndex, columnIndex]);
            }

            classTarget.index_put_(ones(count, device: device), [imageIndex, classIndex, rowIndex, columnIndex]);
            using var assignedColumn = zeros(count, dtype: ScalarType.Int64, device: device);
            assigned.index_put_(ones(count, device: device), [imageIndex, assignedColumn, rowIndex, columnIndex]);
        }

        using var boxTargetDevice = boxTarget;
        using var classTargetDevice = classTarget;
        using var assignedDevice = assigned;
        var boxError = (prediction[.., ..4] - boxTargetDevice).abs() * assignedDevice;
        var boxLoss = boxError.sum() / assignedDevice.sum().clamp_min(1);
        var classLoss = torch.nn.functional.binary_cross_entropy_with_logits(
            prediction[.., 4..],
            classTargetDevice,
            reduction: torch.nn.Reduction.Mean);
        return boxLoss + classLoss;
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
