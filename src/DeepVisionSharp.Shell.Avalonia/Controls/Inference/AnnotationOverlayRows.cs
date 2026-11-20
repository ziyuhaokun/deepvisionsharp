using Avalonia;

namespace DeepVisionSharp.Shell.Avalonia.Controls.Inference;

/// <summary>
/// 画布上的一行检测框，坐标是图像像素坐标（x1,y1,x2,y2 为对角两点，与 deepvision 的 AnnotationRow 同序）。
/// 只保留推理页只读叠加层需要的成员，标注编辑相关的选中/OCR 字段不在这里。
/// </summary>
public sealed class AnnotationRow
{
    public AnnotationRow(Guid id, string label, string color, double x1, double y1, double x2, double y2)
    {
        Id = id;
        Label = label;
        Color = color;
        X1 = x1;
        Y1 = y1;
        X2 = x2;
        Y2 = y2;
    }

    public Guid Id { get; }

    public string Label { get; }

    /// <summary>形如 #3B82F6 的十六进制色值；解析失败时画布退回默认蓝。</summary>
    public string Color { get; }

    public double X1 { get; }

    public double Y1 { get; }

    public double X2 { get; }

    public double Y2 { get; }

    /// <summary>左上角归一的矩形：两个对角点谁大谁小都按同一套绘制。</summary>
    public Rect Bounds => new(
        Math.Min(X1, X2),
        Math.Min(Y1, Y2),
        Math.Abs(X2 - X1),
        Math.Abs(Y2 - Y1));
}

/// <summary>画布上的一块分割多边形，点是图像像素坐标。</summary>
public sealed class SegmentationPolygonRow
{
    public SegmentationPolygonRow(Guid id, string label, string color, IReadOnlyList<Point> points)
    {
        Id = id;
        Label = label;
        Color = color;
        Points = points;
    }

    public Guid Id { get; }

    public string Label { get; }

    /// <summary>形如 #22C55E 的十六进制色值；解析失败时画布退回默认蓝。</summary>
    public string Color { get; }

    public IReadOnlyList<Point> Points { get; }
}
