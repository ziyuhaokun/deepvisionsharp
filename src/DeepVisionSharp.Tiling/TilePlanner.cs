namespace DeepVisionSharp.Tiling;

/// <summary>How an original image is partitioned into source windows.</summary>
public abstract record TileRequest
{
    /// <summary>One window covering the whole image. Network fit is a separate step.</summary>
    public sealed record WholeImage : TileRequest;

    /// <summary>
    /// Sliding windows of the given tile size. OverlapPixels is the shared strip between neighbours.
    /// Remainder follows RemainderPolicy (SAHI default is EdgeAlign).
    /// </summary>
    public sealed record SlidingWindow(
        PixelSize Tile,
        int OverlapPixels,
        RemainderPolicy Remainder = RemainderPolicy.EdgeAlign) : TileRequest;

    /// <summary>
    /// Fixed row by column grid. Cells share the image as evenly as integer division allows;
    /// leftover pixels are spread across the first cells so sizes differ by at most one.
    /// </summary>
    public sealed record Grid(int Rows, int Columns) : TileRequest;

    /// <summary>One source window of Size, anchored in the image. Clipped if larger than the image.</summary>
    public sealed record Crop(PixelSize Size, FitAnchor Anchor = FitAnchor.Center) : TileRequest;
}

/// <summary>One source region on the original image.</summary>
/// <param name="Index">Row-major order, starting at 0.</param>
/// <param name="Source">Intersection of the window with the image (always inside the image).</param>
/// <param name="Canvas">Requested tile canvas. Equals Source.Size except RemainderPolicy.Pad overflow.</param>
/// <param name="Dest">Where <paramref name="Source"/> sits on <paramref name="Canvas"/>. Top-left for Pad overflow (pad is right/bottom).</param>
public sealed record TileWindow(int Index, PixelRect Source, PixelSize Canvas, PixelRect Dest);

/// <summary>Complete partition of an image. Empty plans are never produced.</summary>
public sealed record TilePlan(PixelSize Image, TileRequest Request, IReadOnlyList<TileWindow> Windows);

/// <summary>
/// Geometry-only tile planner. Does not read pixels, OpenCV, or tensors.
/// Classification 1.0 does not call this; it stretch-resizes the whole image in TorchSharp.
/// </summary>
public static class TilePlanner
{
    public static TilePlan Plan(PixelSize image, TileRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var windows = request switch
        {
            TileRequest.WholeImage => PlanWholeImage(image),
            TileRequest.SlidingWindow sliding => PlanSlidingWindow(image, sliding),
            TileRequest.Grid grid => PlanGrid(image, grid),
            TileRequest.Crop crop => PlanCrop(image, crop),
            _ => throw new ArgumentOutOfRangeException(nameof(request), request, "未知的切图请求。")
        };

        return new TilePlan(image, request, windows);
    }

    private static IReadOnlyList<TileWindow> PlanWholeImage(PixelSize image)
    {
        var rect = new PixelRect(0, 0, image.Width, image.Height);
        return [new TileWindow(0, rect, image, rect)];
    }

    private static IReadOnlyList<TileWindow> PlanCrop(PixelSize image, TileRequest.Crop request)
    {
        var width = Math.Min(request.Size.Width, image.Width);
        var height = Math.Min(request.Size.Height, image.Height);
        var x = request.Anchor == FitAnchor.TopLeft ? 0 : (image.Width - width) / 2;
        var y = request.Anchor == FitAnchor.TopLeft ? 0 : (image.Height - height) / 2;
        var source = new PixelRect(x, y, width, height);
        return [new TileWindow(0, source, source.Size, new PixelRect(0, 0, source.Width, source.Height))];
    }

    private static IReadOnlyList<TileWindow> PlanGrid(PixelSize image, TileRequest.Grid request)
    {
        if (request.Rows <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), request.Rows, "行数必须为正。");
        }

        if (request.Columns <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), request.Columns, "列数必须为正。");
        }

        if (request.Rows > image.Height)
        {
            throw new ArgumentOutOfRangeException(nameof(request), request.Rows, $"行数不能超过图像高度（{image.Height}）。");
        }

        if (request.Columns > image.Width)
        {
            throw new ArgumentOutOfRangeException(nameof(request), request.Columns, $"列数不能超过图像宽度（{image.Width}）。");
        }

        var xs = EvenSplits(image.Width, request.Columns);
        var ys = EvenSplits(image.Height, request.Rows);
        var windows = new List<TileWindow>(request.Rows * request.Columns);
        var index = 0;
        for (var row = 0; row < request.Rows; row++)
        {
            for (var column = 0; column < request.Columns; column++)
            {
                var (x, width) = xs[column];
                var (y, height) = ys[row];
                var source = new PixelRect(x, y, width, height);
                windows.Add(new TileWindow(index, source, source.Size, new PixelRect(0, 0, source.Width, source.Height)));
                index++;
            }
        }

        return windows;
    }

    private static IReadOnlyList<TileWindow> PlanSlidingWindow(PixelSize image, TileRequest.SlidingWindow request)
    {
        var tile = request.Tile;
        var overlap = request.OverlapPixels;
        if (overlap < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), overlap, "重叠像素不能为负。");
        }

        if (overlap >= tile.Width || overlap >= tile.Height)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                overlap,
                $"重叠像素必须小于切块边长（{tile}）。");
        }

        var strideX = tile.Width - overlap;
        var strideY = tile.Height - overlap;
        var xs = AxisOrigins(image.Width, tile.Width, strideX, request.Remainder);
        var ys = AxisOrigins(image.Height, tile.Height, strideY, request.Remainder);

        var windows = new List<TileWindow>(xs.Count * ys.Count);
        var index = 0;
        foreach (var y in ys)
        {
            foreach (var x in xs)
            {
                windows.Add(BuildSlidingWindow(index, image, tile, x, y, request.Remainder));
                index++;
            }
        }

        return windows;
    }

    private static TileWindow BuildSlidingWindow(
        int index,
        PixelSize image,
        PixelSize tile,
        int x,
        int y,
        RemainderPolicy remainder)
    {
        var sourceWidth = Math.Min(tile.Width, image.Width - x);
        var sourceHeight = Math.Min(tile.Height, image.Height - y);
        var source = new PixelRect(x, y, sourceWidth, sourceHeight);
        var dest = new PixelRect(0, 0, sourceWidth, sourceHeight);
        var canvas = remainder == RemainderPolicy.Pad ? tile : source.Size;
        return new TileWindow(index, source, canvas, dest);
    }

    private static List<int> AxisOrigins(int imageLength, int tileLength, int stride, RemainderPolicy remainder)
    {
        if (imageLength <= tileLength)
        {
            return [0];
        }

        var origins = new List<int>();
        var origin = 0;
        while (origin + tileLength <= imageLength)
        {
            origins.Add(origin);
            origin += stride;
        }

        if (remainder == RemainderPolicy.Drop)
        {
            return origins.Count > 0 ? origins : [0];
        }

        var last = remainder == RemainderPolicy.Pad ? origin : imageLength - tileLength;
        if (remainder == RemainderPolicy.Pad)
        {
            if (origin < imageLength && (origins.Count == 0 || origins[^1] != origin))
            {
                origins.Add(origin);
            }

            return origins;
        }

        if (origins.Count == 0 || origins[^1] != last)
        {
            origins.Add(last);
        }

        return origins;
    }

    /// <summary>
    /// Split <paramref name="total"/> into <paramref name="parts"/> contiguous spans.
    /// Remainder pixels are spread across the first (total % parts) cells so tile sizes differ by at most one.
    /// </summary>
    private static List<(int Origin, int Length)> EvenSplits(int total, int parts)
    {
        var baseLength = total / parts;
        var extra = total % parts;
        var splits = new List<(int Origin, int Length)>(parts);
        var origin = 0;
        for (var i = 0; i < parts; i++)
        {
            var length = baseLength + (i < extra ? 1 : 0);
            splits.Add((origin, length));
            origin += length;
        }

        return splits;
    }
}
