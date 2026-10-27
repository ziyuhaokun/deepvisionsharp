using DeepVisionSharp.Application.Projects;
using DeepVisionSharp.Domain.Common;

namespace DeepVisionSharp.Application.Ports;

public interface IProjectSqliteTransferService
{
    Task<Result<ProjectTransferResult>> ExportAsync(
        Guid projectId,
        string outputFilePath,
        IProgress<ProjectTransferProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<Result<ProjectTransferResult>> ImportAsync(
        string inputFilePath,
        IProgress<ProjectTransferProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
