using Xunit;
using DeepVisionSharp.Tiling;

namespace DeepVisionSharp.Tiling.Tests;

public sealed class TilePlannerTests
{
    [Fact]
    public void WholeImage_covers_the_source_once()
    {
        var plan = TilePlanner.Plan(new PixelSize(1920, 1080), new TileRequest.WholeImage());

        var window = Assert.Single(plan.Windows);
        Assert.Equal(0, window.Index);
        Assert.Equal(new PixelRect(0, 0, 1920, 1080), window.Source);
        Assert.Equal(new PixelSize(1920, 1080), window.Canvas);
    }

    [Fact]
    public void SlidingWindow_exact_grid_has_no_overlap()
    {
        var plan = TilePlanner.Plan(
            new PixelSize(512, 256),
            new TileRequest.SlidingWindow(new PixelSize(256, 256), OverlapPixels: 0));

        Assert.Equal(2, plan.Windows.Count);
        Assert.Equal(new PixelRect(0, 0, 256, 256), plan.Windows[0].Source);
        Assert.Equal(new PixelRect(256, 0, 256, 256), plan.Windows[1].Source);
    }

    [Fact]
    public void SlidingWindow_edge_align_pins_the_last_origin()
    {
        var plan = TilePlanner.Plan(
            new PixelSize(300, 200),
            new TileRequest.SlidingWindow(new PixelSize(256, 200), OverlapPixels: 0));

        Assert.Equal(2, plan.Windows.Count);
        Assert.Equal(new PixelRect(0, 0, 256, 200), plan.Windows[0].Source);
        Assert.Equal(new PixelRect(44, 0, 256, 200), plan.Windows[1].Source);
        Assert.Equal(300, plan.Windows[1].Source.Right);
        Assert.Equal(new PixelSize(256, 200), plan.Windows[1].Canvas);
    }

    [Fact]
    public void SlidingWindow_drop_skips_the_remainder()
    {
        var plan = TilePlanner.Plan(
            new PixelSize(300, 200),
            new TileRequest.SlidingWindow(
                new PixelSize(256, 200),
                OverlapPixels: 0,
                Remainder: RemainderPolicy.Drop));

        var window = Assert.Single(plan.Windows);
        Assert.Equal(new PixelRect(0, 0, 256, 200), window.Source);
    }

    [Fact]
    public void SlidingWindow_pad_keeps_a_partial_last_source_on_a_full_canvas()
    {
        var plan = TilePlanner.Plan(
            new PixelSize(300, 200),
            new TileRequest.SlidingWindow(
                new PixelSize(256, 200),
                OverlapPixels: 0,
                Remainder: RemainderPolicy.Pad));

        Assert.Equal(2, plan.Windows.Count);
        Assert.Equal(new PixelRect(0, 0, 256, 200), plan.Windows[0].Source);
        Assert.Equal(new PixelRect(256, 0, 44, 200), plan.Windows[1].Source);
        Assert.Equal(new PixelSize(256, 200), plan.Windows[1].Canvas);
        Assert.Equal(new PixelRect(0, 0, 44, 200), plan.Windows[1].Dest);
    }

    [Fact]
    public void SlidingWindow_shrinks_to_the_image_when_the_tile_is_larger()
    {
        var plan = TilePlanner.Plan(
            new PixelSize(100, 80),
            new TileRequest.SlidingWindow(new PixelSize(256, 256), OverlapPixels: 64));

        var window = Assert.Single(plan.Windows);
        Assert.Equal(new PixelRect(0, 0, 100, 80), window.Source);
    }

    [Fact]
    public void SlidingWindow_overlap_shortens_the_stride()
    {
        var plan = TilePlanner.Plan(
            new PixelSize(400, 200),
            new TileRequest.SlidingWindow(new PixelSize(200, 200), OverlapPixels: 50));

        Assert.Equal(3, plan.Windows.Count);
        Assert.Equal(0, plan.Windows[0].Source.X);
        Assert.Equal(150, plan.Windows[1].Source.X);
        Assert.Equal(200, plan.Windows[2].Source.X);
        Assert.All(plan.Windows, window => Assert.Equal(200, window.Source.Width));
    }


    [Fact]
    public void SlidingWindow_pad_on_a_smaller_image_keeps_one_window()
    {
        var plan = TilePlanner.Plan(
            new PixelSize(100, 80),
            new TileRequest.SlidingWindow(
                new PixelSize(256, 256),
                OverlapPixels: 0,
                Remainder: RemainderPolicy.Pad));

        var window = Assert.Single(plan.Windows);
        Assert.Equal(new PixelRect(0, 0, 100, 80), window.Source);
        Assert.Equal(new PixelSize(256, 256), window.Canvas);
        Assert.Equal(new PixelRect(0, 0, 100, 80), window.Dest);
    }

    [Fact]
    public void Every_window_source_stays_inside_the_image_and_matches_dest_size()
    {
        TileRequest[] requests =
        [
            new TileRequest.WholeImage(),
            new TileRequest.SlidingWindow(new PixelSize(256, 192), 32),
            new TileRequest.SlidingWindow(new PixelSize(256, 192), 32, RemainderPolicy.Drop),
            new TileRequest.SlidingWindow(new PixelSize(256, 192), 32, RemainderPolicy.Pad),
            new TileRequest.Grid(3, 4),
            new TileRequest.Crop(new PixelSize(100, 80))
        ];

        foreach (var request in requests)
        {
            var image = new PixelSize(500, 370);
            var plan = TilePlanner.Plan(image, request);
            Assert.NotEmpty(plan.Windows);
            foreach (var window in plan.Windows)
            {
                Assert.True(window.Source.Right <= image.Width, request.ToString());
                Assert.True(window.Source.Bottom <= image.Height, request.ToString());
                Assert.True(window.Dest.Right <= window.Canvas.Width, request.ToString());
                Assert.True(window.Dest.Bottom <= window.Canvas.Height, request.ToString());
                Assert.Equal(window.Source.Size, window.Dest.Size);
            }
        }
    }

    [Fact]
    public void SlidingWindow_rejects_overlap_that_is_not_smaller_than_the_tile()
    {
        var request = new TileRequest.SlidingWindow(new PixelSize(64, 64), OverlapPixels: 64);

        var error = Assert.Throws<ArgumentOutOfRangeException>(
            () => TilePlanner.Plan(new PixelSize(128, 128), request));

        Assert.Contains("重叠像素必须小于切块边长", error.Message);
    }

    [Fact]
    public void Grid_gives_the_remainder_to_the_last_cell()
    {
        var plan = TilePlanner.Plan(new PixelSize(10, 6), new TileRequest.Grid(Rows: 2, Columns: 3));

        Assert.Equal(6, plan.Windows.Count);
        Assert.Equal(new PixelRect(0, 0, 4, 3), plan.Windows[0].Source);
        Assert.Equal(new PixelRect(4, 0, 3, 3), plan.Windows[1].Source);
        Assert.Equal(new PixelRect(7, 0, 3, 3), plan.Windows[2].Source);
        Assert.Equal(new PixelRect(0, 3, 4, 3), plan.Windows[3].Source);
        Assert.Equal(new PixelRect(7, 3, 3, 3), plan.Windows[5].Source);
        Assert.Equal(10, plan.Windows[2].Source.Right);
        Assert.Equal(6, plan.Windows[5].Source.Bottom);
    }

    [Fact]
    public void Grid_union_covers_the_image_without_gaps()
    {
        var plan = TilePlanner.Plan(new PixelSize(1920, 1080), new TileRequest.Grid(4, 5));
        var area = plan.Windows.Sum(window => window.Source.Width * window.Source.Height);
        Assert.Equal(1920 * 1080, area);
        Assert.Equal(20, plan.Windows.Count);
    }

    [Fact]
    public void Crop_center_takes_the_middle()
    {
        var plan = TilePlanner.Plan(
            new PixelSize(100, 80),
            new TileRequest.Crop(new PixelSize(40, 20)));

        var window = Assert.Single(plan.Windows);
        Assert.Equal(new PixelRect(30, 30, 40, 20), window.Source);
    }

    [Fact]
    public void Crop_top_left_starts_at_origin()
    {
        var plan = TilePlanner.Plan(
            new PixelSize(100, 80),
            new TileRequest.Crop(new PixelSize(40, 20), FitAnchor.TopLeft));

        Assert.Equal(new PixelRect(0, 0, 40, 20), Assert.Single(plan.Windows).Source);
    }

    [Fact]
    public void Crop_larger_than_the_image_clamps_to_the_image()
    {
        var plan = TilePlanner.Plan(
            new PixelSize(30, 20),
            new TileRequest.Crop(new PixelSize(100, 100)));

        Assert.Equal(new PixelRect(0, 0, 30, 20), Assert.Single(plan.Windows).Source);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(10, 0)]
    [InlineData(-1, 10)]
    public void PixelSize_rejects_non_positive_edges(int width, int height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PixelSize(width, height));
    }
}

public sealed class ImageFitTests
{
    [Fact]
    public void Stretch_scales_axes_independently()
    {
        var fit = ImageFits.Stretch(new PixelSize(200, 100), new PixelSize(50, 50));

        Assert.Equal(ImageFitKind.Stretch, fit.Kind);
        Assert.Equal(0.25, fit.ScaleX);
        Assert.Equal(0.5, fit.ScaleY);
        Assert.Equal(new PixelRect(0, 0, 200, 100), fit.SourceRect);
        Assert.Equal(new PixelRect(0, 0, 50, 50), fit.DestRect);
        Assert.Equal(new PixelSize(50, 50), fit.Canvas);
    }

    [Fact]
    public void Letterbox_preserves_aspect_and_centres_on_the_canvas()
    {
        var fit = ImageFits.Letterbox(new PixelSize(200, 100), new PixelSize(100, 100));

        Assert.Equal(ImageFitKind.Letterbox, fit.Kind);
        Assert.Equal(0.5, fit.ScaleX);
        Assert.Equal(0.5, fit.ScaleY);
        Assert.Equal(new PixelRect(0, 25, 100, 50), fit.DestRect);
        Assert.Equal(100, fit.DestRect.Width + fit.DestRect.X * 2);
    }

    [Fact]
    public void Letterbox_on_an_already_matching_size_has_no_pad()
    {
        var fit = ImageFits.Letterbox(new PixelSize(256, 256), new PixelSize(256, 256));

        Assert.Equal(1, fit.ScaleX);
        Assert.Equal(new PixelRect(0, 0, 256, 256), fit.DestRect);
    }

    [Fact]
    public void Cover_crops_the_overflow_after_uniform_scale()
    {
        var fit = ImageFits.Cover(new PixelSize(200, 100), new PixelSize(50, 50));

        Assert.Equal(ImageFitKind.Cover, fit.Kind);
        Assert.Equal(0.5, fit.ScaleX);
        Assert.Equal(new PixelSize(50, 50), fit.Canvas);
        Assert.Equal(0, fit.DestRect.X);
        Assert.Equal(new PixelRect(50, 0, 100, 100), fit.SourceRect);
    }

    [Fact]
    public void Pad_does_not_scale_and_grows_the_canvas()
    {
        var fit = ImageFits.Pad(new PixelSize(30, 10), new PixelSize(40, 40));

        Assert.Equal(1, fit.ScaleX);
        Assert.Equal(new PixelSize(40, 40), fit.Canvas);
        Assert.Equal(new PixelRect(5, 15, 30, 10), fit.DestRect);
    }

    [Fact]
    public void Pad_leaves_a_larger_source_unchanged()
    {
        var fit = ImageFits.Pad(new PixelSize(80, 60), new PixelSize(40, 40));

        Assert.Equal(new PixelSize(80, 60), fit.Canvas);
        Assert.Equal(new PixelRect(0, 0, 80, 60), fit.DestRect);
    }

    [Fact]
    public void ShortEdge_matches_the_short_side()
    {
        var fit = ImageFits.ShortEdge(new PixelSize(200, 100), 50);

        Assert.Equal(new PixelSize(100, 50), fit.Canvas);
        Assert.Equal(0.5, fit.ScaleX);
    }

    [Fact]
    public void ShortEdge_clamps_by_max_long_edge()
    {
        var fit = ImageFits.ShortEdge(new PixelSize(200, 100), shortEdge: 80, maxLongEdge: 100);

        Assert.Equal(new PixelSize(100, 50), fit.Canvas);
    }

    [Fact]
    public void LongEdge_matches_the_long_side()
    {
        var fit = ImageFits.LongEdge(new PixelSize(200, 100), 50);

        Assert.Equal(new PixelSize(50, 25), fit.Canvas);
    }


    [Fact]
    public void Compute_rejects_short_and_long_edge_kinds()
    {
        var source = new PixelSize(200, 100);
        var target = new PixelSize(64, 64);
        Assert.Throws<ArgumentException>(() => ImageFits.Compute(source, target, ImageFitKind.ShortEdge));
        Assert.Throws<ArgumentException>(() => ImageFits.Compute(source, target, ImageFitKind.LongEdge));
    }

    [Fact]
    public void AlignUp_ceils_to_the_stride()
    {
        Assert.Equal(new PixelSize(640, 384), PixelSizes.AlignUp(new PixelSize(640, 360), 32));
        Assert.Equal(32, PixelSizes.AlignUp(1, 32));
        Assert.Equal(new PixelSize(15, 15), PixelSizes.AlignUp(new PixelSize(15, 15), 1));
    }

    [Fact]
    public void Letterbox_then_align_matches_yolo_style_canvas()
    {
        var source = new PixelSize(1920, 1080);
        var aligned = PixelSizes.AlignUp(new PixelSize(640, 360), 32);
        var fit = ImageFits.Letterbox(source, aligned);

        Assert.Equal(new PixelSize(640, 384), fit.Canvas);
        Assert.Equal(0, fit.DestRect.X);
        Assert.True(fit.DestRect.Y >= 0);
        Assert.Equal(640, fit.DestRect.Width);
    }
}

public sealed class CoordinateMapTests
{
    [Fact]
    public void Letterbox_round_trips_the_source_origin()
    {
        var fit = ImageFits.Letterbox(new PixelSize(200, 100), new PixelSize(100, 100));
        var back = CoordinateMaps.ToSource(fit, fit.DestRect.Origin);
        Assert.Equal(new PixelPoint(0, 0), back);
    }

    [Fact]
    public void Stretch_maps_the_far_corner_onto_the_canvas()
    {
        var fit = ImageFits.Stretch(new PixelSize(200, 100), new PixelSize(50, 25));
        var mapped = CoordinateMaps.ToCanvas(fit, new PixelPoint(200, 100));
        Assert.Equal(new PixelPoint(50, 25), mapped);
    }

    [Fact]
    public void Letterbox_padding_does_not_map_back_to_the_source()
    {
        var fit = ImageFits.Letterbox(new PixelSize(200, 100), new PixelSize(100, 100));

        Assert.False(CoordinateMaps.TryToSource(fit, new PixelPoint(0, 0), out _));
        Assert.Equal(new PixelPoint(0, 0), CoordinateMaps.ToSource(fit, fit.DestRect.Origin));
        Assert.Null(CoordinateMaps.ToSource(fit, new PixelRect(0, 0, 100, 10)));
    }
}
