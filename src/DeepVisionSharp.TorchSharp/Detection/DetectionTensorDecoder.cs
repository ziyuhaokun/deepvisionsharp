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

        // Own every intermediate tensor even when the caller has no dispose scope.
        using var scope = NewDisposeScope();
        using var scores = prediction[0, 4..].sigmoid();
        var (values, indices) = scores.max(dim: 0);
        using var _ = values;
        using var __ = indices;
        using var mask = values > scoreThreshold;
        using var selected = mask.nonzero();
        var count = (int)selected.shape[0];
        if (count == 0)
        {
            return Array.Empty<ScoredDetection>();
        }

        // Bulk copies to CPU: reading each cell with .item() allocates a scalar tensor and
        // synchronises the GPU eight times per candidate, which dominates at 640 px.
        using var selectedCpu = selected.device.type == DeviceType.CPU ? selected.alias() : selected.cpu();
        using var valuesCpu = values.device.type == DeviceType.CPU ? values.alias() : values.cpu();
        using var indicesCpu = indices.device.type == DeviceType.CPU ? indices.alias() : indices.cpu();
        using var predictionCpu = prediction.device.type == DeviceType.CPU ? prediction.alias() : prediction.cpu();
        var cells = selectedCpu.data<long>().ToArray();
        var scoreValues = valuesCpu.data<float>().ToArray();
        var classIndices = indicesCpu.data<long>().ToArray();
        var raw = predictionCpu.data<float>().ToArray();

        var width = (int)prediction.shape[3];
        var plane = (int)prediction.shape[2] * width;
        var result = new List<ScoredDetection>(count);
        for (var index = 0; index < count; index++)
        {
            var cellY = (int)cells[index * 2];
            var cellX = (int)cells[index * 2 + 1];
            var offset = (cellY * width) + cellX;
            var centreX = (cellX + 0.5f) * stride;
            var centreY = (cellY + 0.5f) * stride;
            var left = raw[offset] * stride;
            var top = raw[plane + offset] * stride;
            var right = raw[(2 * plane) + offset] * stride;
            var bottom = raw[(3 * plane) + offset] * stride;
            result.Add(new ScoredDetection(
                (int)classIndices[offset],
                centreX - left,
                centreY - top,
                left + right,
                top + bottom,
                scoreValues[offset]));
        }

        return result;
    }
}
