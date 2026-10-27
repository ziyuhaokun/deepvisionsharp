namespace DeepVisionSharp.Domain.Entities;

public sealed class GallerySamplePage
{
    public GallerySamplePage(IReadOnlyList<GallerySample> items, int totalCount, int skip)
    {
        Items = items;
        TotalCount = totalCount;
        Skip = skip;
    }

    public IReadOnlyList<GallerySample> Items { get; }

    public int TotalCount { get; }

    public int Skip { get; }
}
