using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Tiling;

namespace DeepVisionSharp.Application.Services;

public sealed class TilingService : ITilingService
{
    public TilePlan PlanGrid(int imageWidth, int imageHeight, int rows, int columns) =>
        TilePlanner.Plan(new PixelSize(imageWidth, imageHeight), new TileRequest.Grid(rows, columns));

    public TilePlan PlanSlidingWindow(int imageWidth, int imageHeight, int tileWidth, int tileHeight, int overlapPixels) =>
        TilePlanner.Plan(
            new PixelSize(imageWidth, imageHeight),
            new TileRequest.SlidingWindow(new PixelSize(tileWidth, tileHeight), overlapPixels));

    public TilePlan PlanCrop(int imageWidth, int imageHeight, int cropWidth, int cropHeight, FitAnchor anchor = FitAnchor.Center) =>
        TilePlanner.Plan(
            new PixelSize(imageWidth, imageHeight),
            new TileRequest.Crop(new PixelSize(cropWidth, cropHeight), anchor));
}
