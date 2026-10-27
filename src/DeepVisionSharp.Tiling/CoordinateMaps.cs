namespace DeepVisionSharp.Tiling;

/// <summary>
/// Maps pixels between source image space and a fitted canvas.
/// Inverse of <see cref="ImageFits"/>: used to project model output back onto the original image.
/// Padding and cropped-away canvas pixels do not map onto the source.
/// </summary>
public static class CoordinateMaps
{
    public static PixelPoint ToCanvas(ImageFit fit, PixelPoint sourcePoint)
    {
        ArgumentNullException.ThrowIfNull(fit);
        var x = fit.DestRect.X + (int)Math.Round((sourcePoint.X - fit.SourceRect.X) * fit.ScaleX);
        var y = fit.DestRect.Y + (int)Math.Round((sourcePoint.Y - fit.SourceRect.Y) * fit.ScaleY);
        return new PixelPoint(x, y);
    }

    /// <summary>
    /// Maps a canvas point that lies on <see cref="ImageFit.DestRect"/> (inclusive far edge) back to the source.
    /// Returns false for padding / cropped-away canvas pixels.
    /// </summary>
    public static bool TryToSource(ImageFit fit, PixelPoint canvasPoint, out PixelPoint sourcePoint)
    {
        ArgumentNullException.ThrowIfNull(fit);
        if (!IsOnDest(fit.DestRect, canvasPoint))
        {
            sourcePoint = default;
            return false;
        }

        sourcePoint = MapToSource(fit, canvasPoint);
        return true;
    }

    public static PixelPoint ToSource(ImageFit fit, PixelPoint canvasPoint)
    {
        if (TryToSource(fit, canvasPoint, out var sourcePoint))
        {
            return sourcePoint;
        }

        throw new ArgumentOutOfRangeException(
            nameof(canvasPoint),
            canvasPoint,
            $"画布坐标 {canvasPoint} 不在有效内容区 {fit.DestRect} 内（留白或裁掉的区域不能映回原图）。");
    }

    /// <summary>
    /// Maps a canvas rectangle through the fitted content region. Null when it does not overlap DestRect.
    /// </summary>
    public static PixelRect? ToSource(ImageFit fit, PixelRect canvasRect)
    {
        ArgumentNullException.ThrowIfNull(fit);
        var clipped = canvasRect.Intersect(fit.DestRect);
        if (clipped is null)
        {
            return null;
        }

        var topLeft = MapToSource(fit, clipped.Value.Origin);
        var bottomRight = MapToSource(
            fit,
            new PixelPoint(clipped.Value.Right, clipped.Value.Bottom));
        var x = Math.Min(topLeft.X, bottomRight.X);
        var y = Math.Min(topLeft.Y, bottomRight.Y);
        var width = Math.Max(1, Math.Abs(bottomRight.X - topLeft.X));
        var height = Math.Max(1, Math.Abs(bottomRight.Y - topLeft.Y));
        var mapped = new PixelRect(x, y, width, height);
        return mapped.Intersect(new PixelRect(
            fit.SourceRect.X,
            fit.SourceRect.Y,
            fit.SourceRect.Width,
            fit.SourceRect.Height)) ?? mapped;
    }

    private static PixelPoint MapToSource(ImageFit fit, PixelPoint canvasPoint)
    {
        var x = fit.SourceRect.X + (int)Math.Round((canvasPoint.X - fit.DestRect.X) / fit.ScaleX);
        var y = fit.SourceRect.Y + (int)Math.Round((canvasPoint.Y - fit.DestRect.Y) / fit.ScaleY);
        x = Math.Clamp(x, fit.SourceRect.X, fit.SourceRect.Right);
        y = Math.Clamp(y, fit.SourceRect.Y, fit.SourceRect.Bottom);
        return new PixelPoint(x, y);
    }

    private static bool IsOnDest(PixelRect dest, PixelPoint point) =>
        point.X >= dest.X
        && point.Y >= dest.Y
        && point.X <= dest.Right
        && point.Y <= dest.Bottom;
}
