namespace DeepVisionSharp.Application.Ports;

public interface ISampleThumbnailService
{
    Task<byte[]?> GetThumbnailAsync(
        string imagePath,
        int maxEdgeLength = 200,
        CancellationToken cancellationToken = default);

    void ClearCache();
}
