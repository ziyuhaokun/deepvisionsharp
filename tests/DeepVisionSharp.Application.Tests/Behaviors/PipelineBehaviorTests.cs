using Xunit;
using MediatR;
using FluentValidation;
using Microsoft.Extensions.Logging.Abstractions;
using DeepVisionSharp.Application.Behaviors;
using DeepVisionSharp.Application.Common;
using DeepVisionSharp.Domain.Common;

namespace DeepVisionSharp.Application.Tests.Behaviors;

public sealed class PipelineBehaviorTests
{
    private sealed record DummyCommand : ICommand;

    [Fact]
    public async Task UnhandledExceptionBehavior_maps_ValidationException_to_Failure()
    {
        var behavior = new UnhandledExceptionBehavior<DummyCommand, Result>(
            NullLogger<UnhandledExceptionBehavior<DummyCommand, Result>>.Instance);

        var result = await behavior.Handle(
            new DummyCommand(),
            () => throw new ValidationException("参数不合法"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("参数不合法", result.Error);
    }

    [Fact]
    public async Task UnhandledExceptionBehavior_rethrows_OperationCanceledException_when_token_cancelled()
    {
        var behavior = new UnhandledExceptionBehavior<DummyCommand, Result>(
            NullLogger<UnhandledExceptionBehavior<DummyCommand, Result>>.Instance);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await behavior.Handle(
                new DummyCommand(),
                () => throw new OperationCanceledException(cts.Token),
                cts.Token);
        });
    }

    [Fact]
    public async Task UnhandledExceptionBehavior_maps_IOException_to_Failure()
    {
        var behavior = new UnhandledExceptionBehavior<DummyCommand, Result>(
            NullLogger<UnhandledExceptionBehavior<DummyCommand, Result>>.Instance);

        var result = await behavior.Handle(
            new DummyCommand(),
            () => throw new IOException("磁盘写入异常"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("文件操作失败", result.Error);
    }

    private sealed record TokenProbe : IRequest<MediatR.Unit>;

    private sealed class TokenProbeHandler : IRequestHandler<TokenProbe, MediatR.Unit>
    {
        public CancellationToken Observed { get; private set; }

        public Task<MediatR.Unit> Handle(TokenProbe request, CancellationToken cancellationToken)
        {
            Observed = cancellationToken;
            return Task.FromResult(MediatR.Unit.Value);
        }
    }

    /// <summary>
    /// MediatR 11 passes the token directly into the handler closure; behaviors do not strip it.
    /// Guards against a future upgrade silently changing that contract.
    /// </summary>
    [Fact]
    public async Task MediatR_pipeline_forwards_the_cancellation_token_to_the_handler()
    {
        var handler = new TokenProbeHandler();
        var mediator = new Mediator(type =>
        {
            if (type == typeof(IRequestHandler<TokenProbe, MediatR.Unit>))
            {
                return handler;
            }

            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                return Array.CreateInstance(type.GetGenericArguments()[0], 0);
            }

            return null!; // MediatR asks only for the handler and behaviors above.
        });

        using var cts = new CancellationTokenSource();
        await mediator.Send(new TokenProbe(), cts.Token);

        Assert.Equal(cts.Token, handler.Observed);
    }
}
