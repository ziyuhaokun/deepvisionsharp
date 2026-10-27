using DeepVisionSharp.Application.Common;
using DeepVisionSharp.Domain.Common;
using DeepVisionSharp.Domain.ValueObjects;

namespace DeepVisionSharp.Application.Training;

/// <summary>
/// 启动训练。<c>Resume</c> 仅为兼容历史调用方保留：分类产品不支持续训，传 true 会被校验拒绝。
/// </summary>
public sealed record StartTrainingJobCommand(
    Guid ProjectId,
    Guid JobId,
    TrainingJobForm Form,
    bool Resume = false) : ICommand<StartTrainingJobResult>;

public sealed record StartTrainingJobResult(
    string ModelType,
    string DataRoot,
    string OutputRoot,
    string LogFile,
    DateTime StartedAt,
    IReadOnlyList<string> InformationalMessages);

public sealed class StartTrainingJobCommandHandler : ICommandHandler<StartTrainingJobCommand, StartTrainingJobResult>
{
    private readonly ITrainingJobLifecycleService _lifecycle;

    public StartTrainingJobCommandHandler(ITrainingJobLifecycleService lifecycle)
    {
        _lifecycle = lifecycle;
    }

    public async Task<Result<StartTrainingJobResult>> Handle(
        StartTrainingJobCommand request,
        CancellationToken cancellationToken)
    {
        try { return await _lifecycle.StartAsync(request, cancellationToken).ConfigureAwait(false); }
        catch (ProjectBusyException ex) { return Result<StartTrainingJobResult>.Failure(ex.Message); }
    }
}
