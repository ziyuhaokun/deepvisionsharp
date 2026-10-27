using DeepVisionSharp.Application.Common;
using DeepVisionSharp.Domain.Common;

namespace DeepVisionSharp.Application.Training;

public sealed record ReconcileOrphanedTrainingJobsCommand : ICommand<int>;

public sealed class ReconcileOrphanedTrainingJobsCommandHandler
    : ICommandHandler<ReconcileOrphanedTrainingJobsCommand, int>
{
    private readonly ITrainingJobLifecycleService _lifecycle;

    public ReconcileOrphanedTrainingJobsCommandHandler(ITrainingJobLifecycleService lifecycle)
    {
        _lifecycle = lifecycle;
    }

    public Task<Result<int>> Handle(ReconcileOrphanedTrainingJobsCommand request, CancellationToken cancellationToken)
        => _lifecycle.ReconcileOrphanedJobsAsync(cancellationToken);
}
