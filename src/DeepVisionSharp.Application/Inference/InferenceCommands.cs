using DeepVisionSharp.Application.Common;
using DeepVisionSharp.Application.Models;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Domain.Common;

namespace DeepVisionSharp.Application.Inference;

public sealed record RunSingleInferenceCommand(
    string ModelPath,
    string ImagePath,
    InferenceConfig Config,
    string ModelType) : ICommand<InferenceResult>;

public sealed class RunSingleInferenceCommandHandler : ICommandHandler<RunSingleInferenceCommand, InferenceResult>
{
    private readonly IInferenceRunner _inferenceRunner;
    private readonly IInferenceInputFileProbe _fileProbe;

    public RunSingleInferenceCommandHandler(
        IInferenceRunner inferenceRunner,
        IInferenceInputFileProbe fileProbe)
    {
        _inferenceRunner = inferenceRunner;
        _fileProbe = fileProbe;
    }

    public async Task<Result<InferenceResult>> Handle(RunSingleInferenceCommand request, CancellationToken cancellationToken)
    {
        if (!ModelTypeNames.TryNormalize(request.ModelType, out var modelType))
        {
            return Result<InferenceResult>.Failure(
                string.IsNullOrWhiteSpace(request.ModelType)
                    ? "模型类型不能为空"
                    : $"不支持的推理模型类型：{request.ModelType.Trim()}");
        }

        if (!_fileProbe.ModelExists(request.ModelPath))
        {
            return Result<InferenceResult>.Failure("模型文件不存在");
        }

        if (!_fileProbe.ImageExists(request.ImagePath))
        {
            return Result<InferenceResult>.Failure("图像文件不存在");
        }

        var result = await _inferenceRunner.RunSingleAsync(
            request.ModelPath,
            request.ImagePath,
            request.Config,
            modelType,
            cancellationToken);

        return result.Success
            ? Result<InferenceResult>.Success(result)
            : Result<InferenceResult>.Failure(result.Error ?? "推理失败");
    }
}

/// <summary>
/// Stops the inference started under <paramref name="OwnerKey"/>. It is deliberately not process-wide:
/// without an owner it does nothing, so one page cancelling can never kill another page's work.
/// </summary>
public sealed record StopInferenceCommand(string Reason = "User requested", string? OwnerKey = null) : ICommand;

public sealed class StopInferenceCommandHandler : ICommandHandler<StopInferenceCommand>
{
    private readonly IInferenceRunner _inferenceRunner;

    public StopInferenceCommandHandler(IInferenceRunner inferenceRunner)
    {
        _inferenceRunner = inferenceRunner;
    }

    public async Task<Result> Handle(StopInferenceCommand request, CancellationToken cancellationToken)
    {
        await _inferenceRunner.StopAsync(request.OwnerKey, request.Reason, cancellationToken);
        return Result.Success();
    }
}

public sealed record InferenceStatusQuery() : IQuery<bool>;

public sealed class InferenceStatusQueryHandler : IQueryHandler<InferenceStatusQuery, bool>
{
    private readonly IInferenceStatusReader _statusReader;

    public InferenceStatusQueryHandler(IInferenceStatusReader statusReader)
    {
        _statusReader = statusReader;
    }

    public Task<Result<bool>> Handle(InferenceStatusQuery request, CancellationToken cancellationToken)
    {
        return Task.FromResult(Result<bool>.Success(_statusReader.IsInferenceRunning));
    }
}
