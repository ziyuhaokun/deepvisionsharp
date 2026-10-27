using DeepVisionSharp.Application.Common;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Domain.Common;

namespace DeepVisionSharp.Application.Models;

public sealed record GetModelDisplayMetricsQuery(string? CheckpointPath) : IQuery<ModelDisplayMetrics?>;

public sealed class GetModelDisplayMetricsQueryHandler : IQueryHandler<GetModelDisplayMetricsQuery, ModelDisplayMetrics?>
{
    private readonly IModelMetadataReader _metadataReader;

    public GetModelDisplayMetricsQueryHandler(IModelMetadataReader metadataReader)
    {
        _metadataReader = metadataReader;
    }

    public Task<Result<ModelDisplayMetrics?>> Handle(
        GetModelDisplayMetricsQuery request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.CheckpointPath))
        {
            return Task.FromResult(Result<ModelDisplayMetrics?>.Success(null));
        }

        // Reading the .info is synchronous file IO. Callers enumerate the whole model library one
        // model at a time on the UI thread, so a slow or network-backed folder froze the page; the
        // read runs on the thread pool now.
        return Task.Run(
            () => Result<ModelDisplayMetrics?>.Success(_metadataReader.Read(request.CheckpointPath)),
            cancellationToken);
    }
}
