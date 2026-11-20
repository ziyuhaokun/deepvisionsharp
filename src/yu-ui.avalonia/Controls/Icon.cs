using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace YuUI.Controls;

/// <summary>
/// 描边图标：把 24×24 画布上的路径按 FontSize 等比缩放，用 1.75 圆头画笔描出。
/// 与 WPF 版 YuUI.Controls.Icon 的绘制口径完全一致（同样是描边而非填充）。
/// </summary>
public class Icon : TemplatedControl
{
    public static readonly StyledProperty<Geometry?> DataProperty =
        AvaloniaProperty.Register<Icon, Geometry?>(nameof(Data));

    public static readonly StyledProperty<double> ViewSizeProperty =
        AvaloniaProperty.Register<Icon, double>(nameof(ViewSize), IvIcons.CanvasSize);

    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<Icon, double>(nameof(StrokeThickness), 1.75);

    static Icon()
    {
        AffectsRender<Icon>(DataProperty, ViewSizeProperty, StrokeThicknessProperty, ForegroundProperty);
        AffectsMeasure<Icon>(FontSizeProperty);
    }

    public Geometry? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public double ViewSize
    {
        get => GetValue(ViewSizeProperty);
        set => SetValue(ViewSizeProperty, value);
    }

    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var size = double.IsNaN(FontSize) || FontSize <= 0 ? 16d : FontSize;
        return new Size(size, size);
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));

        var geometry = Data;
        var brush = Foreground;
        if (geometry is null || brush is null)
        {
            return;
        }

        var width = Bounds.Width;
        var height = Bounds.Height;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var view = ViewSize;
        var scale = Math.Min(width, height) / view;
        var offsetX = (width - view * scale) / 2;
        var offsetY = (height - view * scale) / 2;

        var pen = new Pen(brush, StrokeThickness)
        {
            LineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };

        using (context.PushTransform(
                   Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(offsetX, offsetY)))
        {
            context.DrawGeometry(null, pen, geometry);
        }
    }
}
