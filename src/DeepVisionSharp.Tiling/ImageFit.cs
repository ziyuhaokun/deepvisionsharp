namespace DeepVisionSharp.Tiling;

/// <summary>How a source rectangle is mapped onto a canvas.</summary>
public enum ImageFitKind
{
    /// <summary>Independent X/Y scale onto the target. Aspect ratio is not preserved. torchvision Resize.</summary>
    Stretch,

    /// <summary>Uniform scale to fit inside the target; leftover canvas is padding. YOLO letterbox / albumentations LongestMaxSize+Pad.</summary>
    Letterbox,

    /// <summary>Uniform scale to cover the target; overflow is cropped. torchvision Resize+CenterCrop.</summary>
    Cover,

    /// <summary>No scale. Source is placed on a canvas at least as large as the source. albumentations PadIfNeeded.</summary>
    Pad,

    /// <summary>Uniform scale so the short edge equals the target short edge. Detectron2 / torchvision short-side resize.</summary>
    ShortEdge,

    /// <summary>Uniform scale so the long edge equals the target long edge. albumentations LongestMaxSize.</summary>
    LongEdge
}

/// <summary>
/// Geometry of a source-to-canvas mapping. Does not choose a pad colour or allocate pixels.
/// SourceRect is the region taken from the original; DestRect is where it lands on the canvas.
/// </summary>
public sealed record ImageFit(
    ImageFitKind Kind,
    PixelSize Source,
    PixelSize Canvas,
    PixelRect SourceRect,
    PixelRect DestRect,
    double ScaleX,
    double ScaleY);

public static class ImageFits
{
    /// <summary>
    /// Fits onto a fixed canvas. ShortEdge / LongEdge take a length, not a canvas, so they have dedicated overloads.
    /// </summary>
    public static ImageFit Compute(PixelSize source, PixelSize target, ImageFitKind kind, FitAnchor anchor = FitAnchor.Center) =>
        kind switch
        {
            ImageFitKind.Stretch => Stretch(source, target),
            ImageFitKind.Letterbox => Letterbox(source, target, anchor),
            ImageFitKind.Cover => Cover(source, target, anchor),
            ImageFitKind.Pad => Pad(source, target, anchor),
            ImageFitKind.ShortEdge => throw new ArgumentException(
                "短边缩放请调用 ImageFits.ShortEdge(source, shortEdge, maxLongEdge)。", nameof(kind)),
            ImageFitKind.LongEdge => throw new ArgumentException(
                "长边缩放请调用 ImageFits.LongEdge(source, longEdge)。", nameof(kind)),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知的适配方式。")
        };

    public static ImageFit Stretch(PixelSize source, PixelSize target) =>
        new(
            ImageFitKind.Stretch,
            source,
            target,
            new PixelRect(0, 0, source.Width, source.Height),
            new PixelRect(0, 0, target.Width, target.Height),
            ScaleX: (double)target.Width / source.Width,
            ScaleY: (double)target.Height / source.Height);

    public static ImageFit Letterbox(PixelSize source, PixelSize target, FitAnchor anchor = FitAnchor.Center)
    {
        var scale = Math.Min(
            (double)target.Width / source.Width,
            (double)target.Height / source.Height);
        var unpaddedWidth = ClampRound(source.Width * scale, target.Width);
        var unpaddedHeight = ClampRound(source.Height * scale, target.Height);
        var (padX, padY) = Place(target.Width, unpaddedWidth, target.Height, unpaddedHeight, anchor);
        return new ImageFit(
            ImageFitKind.Letterbox,
            source,
            target,
            new PixelRect(0, 0, source.Width, source.Height),
            new PixelRect(padX, padY, unpaddedWidth, unpaddedHeight),
            ScaleX: scale,
            ScaleY: scale);
    }

    public static ImageFit Cover(PixelSize source, PixelSize target, FitAnchor anchor = FitAnchor.Center)
    {
        var scale = Math.Max(
            (double)target.Width / source.Width,
            (double)target.Height / source.Height);
        var scaledWidth = Math.Max(target.Width, (int)Math.Round(source.Width * scale));
        var scaledHeight = Math.Max(target.Height, (int)Math.Round(source.Height * scale));
        var cropX = anchor == FitAnchor.TopLeft ? 0 : (scaledWidth - target.Width) / 2;
        var cropY = anchor == FitAnchor.TopLeft ? 0 : (scaledHeight - target.Height) / 2;
        var sourceX = (int)Math.Floor(cropX / scale);
        var sourceY = (int)Math.Floor(cropY / scale);
        var sourceWidth = Math.Min(source.Width - sourceX, (int)Math.Ceiling(target.Width / scale));
        var sourceHeight = Math.Min(source.Height - sourceY, (int)Math.Ceiling(target.Height / scale));
        sourceWidth = Math.Max(1, sourceWidth);
        sourceHeight = Math.Max(1, sourceHeight);
        return new ImageFit(
            ImageFitKind.Cover,
            source,
            target,
            new PixelRect(sourceX, sourceY, sourceWidth, sourceHeight),
            new PixelRect(0, 0, target.Width, target.Height),
            ScaleX: scale,
            ScaleY: scale);
    }

    public static ImageFit Pad(PixelSize source, PixelSize minCanvas, FitAnchor anchor = FitAnchor.Center)
    {
        var canvas = new PixelSize(
            Math.Max(source.Width, minCanvas.Width),
            Math.Max(source.Height, minCanvas.Height));
        var (padX, padY) = Place(canvas.Width, source.Width, canvas.Height, source.Height, anchor);
        return new ImageFit(
            ImageFitKind.Pad,
            source,
            canvas,
            new PixelRect(0, 0, source.Width, source.Height),
            new PixelRect(padX, padY, source.Width, source.Height),
            ScaleX: 1,
            ScaleY: 1);
    }

    public static ImageFit ShortEdge(PixelSize source, int shortEdge, int? maxLongEdge = null)
    {
        if (shortEdge <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(shortEdge), shortEdge, "短边必须为正。");
        }

        if (maxLongEdge is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxLongEdge), maxLongEdge, "长边上限必须为正。");
        }

        var scale = (double)shortEdge / source.ShortEdge;
        if (maxLongEdge is int max && source.LongEdge * scale > max)
        {
            scale = (double)max / source.LongEdge;
        }

        return ScaledCanvas(ImageFitKind.ShortEdge, source, scale);
    }

    public static ImageFit LongEdge(PixelSize source, int longEdge)
    {
        if (longEdge <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(longEdge), longEdge, "长边必须为正。");
        }

        var scale = (double)longEdge / source.LongEdge;
        return ScaledCanvas(ImageFitKind.LongEdge, source, scale);
    }

    private static ImageFit ScaledCanvas(ImageFitKind kind, PixelSize source, double scale)
    {
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));
        var canvas = new PixelSize(width, height);
        return new ImageFit(
            kind,
            source,
            canvas,
            new PixelRect(0, 0, source.Width, source.Height),
            new PixelRect(0, 0, width, height),
            ScaleX: scale,
            ScaleY: scale);
    }

    private static int ClampRound(double value, int max) =>
        Math.Clamp((int)Math.Round(value), 1, max);

    private static (int X, int Y) Place(int canvasWidth, int contentWidth, int canvasHeight, int contentHeight, FitAnchor anchor)
    {
        var x = anchor == FitAnchor.TopLeft ? 0 : (canvasWidth - contentWidth) / 2;
        var y = anchor == FitAnchor.TopLeft ? 0 : (canvasHeight - contentHeight) / 2;
        return (x, y);
    }
}
