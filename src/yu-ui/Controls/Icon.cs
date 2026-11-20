using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace YuUI.Controls;

/// <summary>
/// 描边图标：把 24×24 画布上的路径等比缩放到 <see cref="ViewSize"/>，用 <see cref="StrokeThickness"/> 的圆头画笔描出。
/// 源 Icon.cs 在 Render 里做的是「几何描边」而非填充，缩放与居中口径见 Icon.xaml 的 Viewbox。
/// </summary>
public class Icon : Control
{
    public static readonly DependencyProperty DataProperty =
        DependencyProperty.Register(
            nameof(Data),
            typeof(Geometry),
            typeof(Icon),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ViewSizeProperty =
        DependencyProperty.Register(
            nameof(ViewSize),
            typeof(double),
            typeof(Icon),
            new FrameworkPropertyMetadata(
                IvIcons.CanvasSize,
                FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeThicknessProperty =
        DependencyProperty.Register(
            nameof(StrokeThickness),
            typeof(double),
            typeof(Icon),
            new FrameworkPropertyMetadata(1.75d, FrameworkPropertyMetadataOptions.AffectsRender));

    static Icon()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(Icon), new FrameworkPropertyMetadata(typeof(Icon)));
    }

    public Geometry? Data
    {
        get => (Geometry?)GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public double ViewSize
    {
        get => (double)GetValue(ViewSizeProperty);
        set => SetValue(ViewSizeProperty, value);
    }

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }
}
