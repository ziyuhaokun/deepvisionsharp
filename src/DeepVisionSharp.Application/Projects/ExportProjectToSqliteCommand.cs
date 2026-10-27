using DeepVisionSharp.Application.Common;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Domain.Common;

namespace DeepVisionSharp.Application.Projects;

public sealed record ExportProjectToSqliteCommand(
    Guid ProjectId,
    string OutputFilePath,
    IProgress<ProjectTransferProgress>? Progress = null) : ICommand<ProjectTransferResult>;

public sealed class ExportProjectToSqliteCommandHandler : ICommandHandler<ExportProjectToSqliteCommand, ProjectTransferResult>
{
    private readonly IProjectSqliteTransferService _transfer;

    public ExportProjectToSqliteCommandHandler(IProjectSqliteTransferService transfer)
    {
        _transfer = transfer;
    }

    public Task<Result<ProjectTransferResult>> Handle(ExportProjectToSqliteCommand request, CancellationToken cancellationToken)
    {
        return _transfer.ExportAsync(request.ProjectId, request.OutputFilePath, request.Progress, cancellationToken);
    }
}
