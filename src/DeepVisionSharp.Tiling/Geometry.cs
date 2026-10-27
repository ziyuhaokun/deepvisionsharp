namespace DeepVisionSharp.Tiling;

/// <summary>Positive pixel size. Zero or negative edges are invalid.</summary>
public readonly record struct PixelSize
{
    public PixelSize(int width, int height)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "宽度必须为正。");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), height, "高度必须为正。");
        }

        Width = width;
        Height = height;
    }

    public int Width { get; }

    public int Height { get; }

    public int Area => Width * Height;

    public int ShortEdge => Math.Min(Width, Height);

    public int LongEdge => Math.Max(Width, Height);

    public override string ToString() => $"{Width}x{Height}";
}

/// <summary>Integer pixel coordinate. Origin is top-left.</summary>
public readonly record struct PixelPoint(int X, int Y)
{
    public override string ToString() => $"{X},{Y}";
}

/// <summary>Axis-aligned rectangle in image pixels. Origin is top-left. Always inside its declared bounds.</summary>
public readonly record struct PixelRect
{
    public PixelRect(int x, int y, int width, int height)
    {
        if (x < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(x), x, "X 不能为负。");
        }

        if (y < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(y), y, "Y 不能为负。");
        }

        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "宽度必须为正。");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), height, "高度必须为正。");
        }

        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public int X { get; }

    public int Y { get; }

    public int Width { get; }

    public int Height { get; }

    public int Right => X + Width;

    public int Bottom => Y + Height;

    public PixelSize Size => new(Width, Height);

    public PixelPoint Origin => new(X, Y);

    public bool Contains(PixelPoint point) =>
        point.X >= X && point.Y >= Y && point.X < Right && point.Y < Bottom;

    public PixelRect ClampTo(PixelSize bounds) =>
        Intersect(new PixelRect(0, 0, bounds.Width, bounds.Height))
        ?? new PixelRect(
            Math.Min(Math.Max(0, X), Math.Max(0, bounds.Width - 1)),
            Math.Min(Math.Max(0, Y), Math.Max(0, bounds.Height - 1)),
            1,
            1);

    /// <summary>Inclusive origin, exclusive far edge. Null when the rectangles do not overlap.</summary>
    public PixelRect? Intersect(PixelRect other)
    {
        var x = Math.Max(X, other.X);
        var y = Math.Max(Y, other.Y);
        var right = Math.Min(Right, other.Right);
        var bottom = Math.Min(Bottom, other.Bottom);
        if (right <= x || bottom <= y)
        {
            return null;
        }

        return new PixelRect(x, y, right - x, bottom - y);
    }

    public bool Contains(PixelRect other) =>
        other.X >= X && other.Y >= Y && other.Right <= Right && other.Bottom <= Bottom;

    public override string ToString() => $"{X},{Y} {Width}x{Height}";
}

/// <summary>
/// How the last sliding window treats a remainder smaller than the tile.
/// SAHI uses <see cref="EdgeAlign"/>.
/// </summary>
public enum RemainderPolicy
{
    /// <summary>Shift the last origin so the window stays inside the image (SAHI).</summary>
    EdgeAlign,

    /// <summary>Skip windows that would extend past the image. If none fit, fall back to one clipped window.</summary>
    Drop,

    /// <summary>Keep a fixed tile canvas; the overflowing side is padding, source is the in-image intersection.</summary>
    Pad
}

/// <summary>Where a scaled or cropped region sits on the target canvas (or which source region is kept).</summary>
public enum FitAnchor
{
    Center,
    TopLeft
}

/// <summary>Size helpers used before a fit (YOLO stride alignment, Detectron2 max-size clamp).</summary>
public static class PixelSizes
{
    /// <summary>Ceil each edge to a multiple of <paramref name="multiple"/>. multiple &lt;= 1 leaves the size unchanged.</summary>
    public static PixelSize AlignUp(PixelSize size, int multiple)
    {
        if (multiple <= 1)
        {
            return size;
        }

        return new PixelSize(AlignUp(size.Width, multiple), AlignUp(size.Height, multiple));
    }

    public static int AlignUp(int value, int multiple)
    {
        if (multiple <= 1)
        {
            return value;
        }

        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "尺寸必须为正。");
        }

        var aligned = (value + multiple - 1) / multiple * multiple;
        return aligned;
    }
}

