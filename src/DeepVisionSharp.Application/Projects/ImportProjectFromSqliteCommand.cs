using DeepVisionSharp.Application.Common;
using DeepVisionSharp.Application.Galleries;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Domain.Common;

namespace DeepVisionSharp.Application.Projects;

public sealed record ImportProjectFromSqliteCommand(
    string InputFilePath,
    IProgress<ProjectTransferProgress>? Progress = null) : ICommand<ProjectTransferResult>;

public sealed class ImportProjectFromSqliteCommandHandler : ICommandHandler<ImportProjectFromSqliteCommand, ProjectTransferResult>
{
    private readonly IProjectSqliteTransferService _transfer;

    public ImportProjectFromSqliteCommandHandler(IProjectSqliteTransferService transfer)
    {
        _transfer = transfer;
    }

    public Task<Result<ProjectTransferResult>> Handle(
        ImportProjectFromSqliteCommand request,
        CancellationToken cancellationToken) =>
        _transfer.ImportAsync(request.InputFilePath, request.Progress, cancellationToken);
}
