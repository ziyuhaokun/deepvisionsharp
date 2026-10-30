using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

using DeepVisionSharp.Shell.ViewModels.Labeling;

namespace DeepVisionSharp.Shell.Controls;

/// <summary>
/// Bird's-eye view of the current sample: the whole image with every box and the visible viewport.
/// Clicking a box selects it in the editor.
/// </summary>
public sealed class BoxOverviewCanvas : FrameworkElement
{
    public static readonly DependencyProperty ImageSourceProperty = DependencyProperty.Register(
        nameof(ImageSource), typeof(ImageSource), typeof(BoxOverviewCanvas),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BoxesProperty = DependencyProperty.Register(
        nameof(Boxes), typeof(IList), typeof(BoxOverviewCanvas),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SelectedBoxProperty = DependencyProperty.Register(
        nameof(SelectedBox), typeof(BoxShapeViewModel), typeof(BoxOverviewCanvas),
        new FrameworkPropertyMetadata(
            null,
            FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty ViewportRectProperty = DependencyProperty.Register(
        nameof(ViewportRect), typeof(Rect), typeof(BoxOverviewCanvas),
        new FrameworkPropertyMetadata(Rect.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    public ImageSource? ImageSource
    {
        get => (ImageSource?)GetValue(ImageSourceProperty);
        set => SetValue(ImageSourceProperty, value);
    }

    public IList? Boxes
    {
        get => (IList?)GetValue(BoxesProperty);
        set => SetValue(BoxesProperty, value);
    }

    public BoxShapeViewModel? SelectedBox
    {
        get => (BoxShapeViewModel?)GetValue(SelectedBoxProperty);
        set => SetValue(SelectedBoxProperty, value);
    }

    public Rect ViewportRect
    {
        get => (Rect)GetValue(ViewportRectProperty);
        set => SetValue(ViewportRectProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        drawingContext.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));

        var content = GetContentRect();
        if (ImageSource is null || content.Width <= 0)
        {
            DrawEmpty(drawingContext);
            return;
        }

        drawingContext.DrawImage(ImageSource, content);
        var scale = content.Width / Math.Max(1, ImageSource.Width);

        if (Boxes is not null)
        {
            foreach (var box in Boxes.OfType<BoxShapeViewModel>())
            {
                var rect = new Rect(
                    content.X + (box.X * scale),
                    content.Y + (box.Y * scale),
                    Math.Max(1, box.Width * scale),
                    Math.Max(1, box.Height * scale));
                var color = ParseColor(box.Color);
                var selected = ReferenceEquals(box, SelectedBox);
                drawingContext.DrawRectangle(
                    selected ? new SolidColorBrush(Color.FromArgb(0x26, color.R, color.G, color.B)) : null,
                    CreatePen(color, selected ? 2 : 1),
                    rect);
            }
        }

        if (ViewportRect is { Width: > 0, Height: > 0 } viewport)
        {
            var rect = new Rect(
                content.X + (viewport.X * scale),
                content.Y + (viewport.Y * scale),
                Math.Max(2, viewport.Width * scale),
                Math.Max(2, viewport.Height * scale));
            var pen = new Pen(Brushes.White, 1.5)
            {
                DashStyle = new DashStyle([3, 3], 0)
            };
            pen.Freeze();
            drawingContext.DrawRectangle(null, pen, rect);
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (Boxes is null || ImageSource is null)
        {
            return;
        }

        var content = GetContentRect();
        var scale = content.Width / Math.Max(1, ImageSource.Width);
        if (scale <= 0)
        {
            return;
        }

        var point = e.GetPosition(this);
        var imageX = (point.X - content.X) / scale;
        var imageY = (point.Y - content.Y) / scale;
        BoxShapeViewModel? found = null;
        foreach (var box in Boxes.OfType<BoxShapeViewModel>())
        {
            if (imageX >= box.X && imageX <= box.X + box.Width
                && imageY >= box.Y && imageY <= box.Y + box.Height)
            {
                found = box;
            }
        }

        if (found is not null)
        {
            SelectedBox = found;
            e.Handled = true;
        }
    }

    private Rect GetContentRect()
    {
        var source = ImageSource;
        if (source is null || source.Width <= 0 || source.Height <= 0
            || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return Rect.Empty;
        }

        var scale = Math.Min(ActualWidth / source.Width, ActualHeight / source.Height);
        var width = source.Width * scale;
        var height = source.Height * scale;
        return new Rect((ActualWidth - width) / 2, (ActualHeight - height) / 2, width, height);
    }

    private void DrawEmpty(DrawingContext context)
    {
        var text = new FormattedText(
            "无预览",
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface("Microsoft YaHei UI"),
            12,
            new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)),
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        context.DrawText(text, new Point(
            (ActualWidth - text.Width) / 2,
            (ActualHeight - text.Height) / 2));
    }

    private static Pen CreatePen(Color color, double thickness)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        var pen = new Pen(brush, thickness);
        pen.Freeze();
        return pen;
    }

    private static Color ParseColor(string value)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(value);
        }
        catch
        {
            return Color.FromRgb(0x3B, 0x82, 0xF6);
        }
    }
}
