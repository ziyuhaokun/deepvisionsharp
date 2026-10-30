namespace DeepVisionSharp.TorchSharp.Detection;

/// <summary>One decoded detection in canvas pixels.</summary>
public readonly record struct ScoredDetection(
    int ClassIndex,
    float X,
    float Y,
    float Width,
    float Height,
    float Score);

/// <summary>Per-class greedy non-maximum suppression.</summary>
public static class DetectionNms
{
    public static IReadOnlyList<ScoredDetection> Apply(
        IEnumerable<ScoredDetection> detections,
        float iouThreshold,
        int maxDetections)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxDetections, 1);
        var kept = new List<ScoredDetection>();
        foreach (var group in detections
                     .OrderByDescending(item => item.Score)
                     .GroupBy(item => item.ClassIndex))
        {
            var candidates = new List<ScoredDetection>();
            foreach (var candidate in group)
            {
                if (candidates.Any(existing => IoU(existing, candidate) > iouThreshold))
                {
                    continue;
                }

                candidates.Add(candidate);
                if (kept.Count + candidates.Count >= maxDetections)
                {
                    break;
                }
            }

            kept.AddRange(candidates);
            if (kept.Count >= maxDetections)
            {
                break;
            }
        }

        return kept
            .OrderByDescending(item => item.Score)
            .Take(maxDetections)
            .ToArray();
    }

    public static float IoU(ScoredDetection first, ScoredDetection second)
    {
        var left = Math.Max(first.X, second.X);
        var top = Math.Max(first.Y, second.Y);
        var right = Math.Min(first.X + first.Width, second.X + second.Width);
        var bottom = Math.Min(first.Y + first.Height, second.Y + second.Height);
        var intersection = Math.Max(0, right - left) * Math.Max(0, bottom - top);
        if (intersection <= 0)
        {
            return 0;
        }

        var union = (first.Width * first.Height) + (second.Width * second.Height) - intersection;
        return union <= 0 ? 0 : intersection / union;
    }
}
