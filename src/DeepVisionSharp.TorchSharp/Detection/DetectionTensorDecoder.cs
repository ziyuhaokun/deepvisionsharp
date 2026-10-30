using TorchSharp;
using static TorchSharp.torch;
using DeepVisionSharp.TorchSharp.Detection.Yolo26;

namespace DeepVisionSharp.TorchSharp.Detection;

/// <summary>
/// Tensor-first decode for production inference. The per-cell scalar loop in
/// <see cref="DetectionDecoder"/> is fine for tests but far too slow at 640 px, so this reads only
/// the cells that pass the score threshold.
/// </summary>
public static class DetectionTensorDecoder
{
    public static IReadOnlyList<ScoredDetection> DecodeAll(
        IReadOnlyList<Tensor> predictions,
        int baseStride,
        float scoreThreshold,
        float iouThreshold,
        int maxDetections)
    {
        var found = new List<ScoredDetection>();
        for (var scale = 0; scale < predictions.Count; scale++)
        {
            found.AddRange(DecodeScale(predictions[scale], baseStride << scale, scoreThreshold));
        }

        return DetectionNms.Apply(found, iouThreshold, maxDetections);
    }

    public static IReadOnlyList<ScoredDetection> DecodeScale(
        Tensor prediction,
        int stride,
        float scoreThreshold)
    {
        if (prediction.dim() != 4)
        {
            throw new ArgumentException("检测输出必须是 (batch, 4 + classes, height, width)。", nameof(prediction));
        }

        using var scores = prediction[0, 4..].sigmoid();
        var (values, indices) = scores.max(dim: 0);
        using var _ = values;
        using var __ = indices;
        using var mask = values > scoreThreshold;
        using var selected = mask.nonzero();
        var count = (int)selected.shape[0];
        var result = new List<ScoredDetection>(count);
        for (var index = 0; index < count; index++)
        {
            var cellY = (int)selected[index, 0].item<long>();
            var cellX = (int)selected[index, 1].item<long>();
            var classIndex = (int)indices[cellY, cellX].item<long>();
            var score = values[cellY, cellX].item<float>();
            var centreX = (cellX + 0.5f) * stride;
            var centreY = (cellY + 0.5f) * stride;
            var left = prediction[0, 0, cellY, cellX].item<float>() * stride;
            var top = prediction[0, 1, cellY, cellX].item<float>() * stride;
            var right = prediction[0, 2, cellY, cellX].item<float>() * stride;
            var bottom = prediction[0, 3, cellY, cellX].item<float>() * stride;
            result.Add(new ScoredDetection(
                classIndex,
                centreX - left,
                centreY - top,
                left + right,
                top + bottom,
                score));
        }

        return result;
    }
}
