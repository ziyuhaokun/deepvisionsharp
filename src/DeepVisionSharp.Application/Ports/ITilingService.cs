using DeepVisionSharp.Tiling;

namespace DeepVisionSharp.Application.Ports;

/// <summary>
/// Application service for spatial tiling and sliding-window image partitioning.
/// </summary>
public interface ITilingService
{
    TilePlan PlanGrid(int imageWidth, int imageHeight, int rows, int columns);

    TilePlan PlanSlidingWindow(int imageWidth, int imageHeight, int tileWidth, int tileHeight, int overlapPixels);

    TilePlan PlanCrop(int imageWidth, int imageHeight, int cropWidth, int cropHeight, FitAnchor anchor = FitAnchor.Center);
}
