using DeepVisionSharp.Application.Common;
using DeepVisionSharp.Domain.Common;

namespace DeepVisionSharp.Application.Training;

public sealed record StopTrainingJobCommand(Guid JobId) : ICommand;

public sealed class StopTrainingJobCommandHandler : ICommandHandler<StopTrainingJobCommand>
{
    private readonly ITrainingJobLifecycleService _lifecycle;

    public StopTrainingJobCommandHandler(ITrainingJobLifecycleService lifecycle)
    {
        _lifecycle = lifecycle;
    }

    public Task<Result> Handle(StopTrainingJobCommand request, CancellationToken cancellationToken)
        => _lifecycle.StopAsync(request.JobId, cancellationToken);
}
