namespace DeepVisionSharp.Domain.Repositories;

public sealed class GallerySampleFilter
{
    public bool? IsAnnotated { get; init; }

    public bool? HasError { get; init; }

    public string? FileNamePrefix { get; init; }
}
