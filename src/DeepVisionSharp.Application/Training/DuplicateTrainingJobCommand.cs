using DeepVisionSharp.Application.Common;
using DeepVisionSharp.Domain.Common;

namespace DeepVisionSharp.Application.Training;

public sealed record DuplicateTrainingJobCommand(Guid SourceJobId, Guid? TargetProjectId = null) : ICommand<Guid>;

public sealed class DuplicateTrainingJobCommandHandler : ICommandHandler<DuplicateTrainingJobCommand, Guid>
{
    private readonly ITrainingJobLifecycleService _lifecycle;

    public DuplicateTrainingJobCommandHandler(ITrainingJobLifecycleService lifecycle)
    {
        _lifecycle = lifecycle;
    }

    public Task<Result<Guid>> Handle(DuplicateTrainingJobCommand request, CancellationToken cancellationToken)
        => _lifecycle.DuplicateAsDraftAsync(request.SourceJobId, request.TargetProjectId, cancellationToken);
}
