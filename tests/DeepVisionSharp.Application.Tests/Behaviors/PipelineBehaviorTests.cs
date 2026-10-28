using Xunit;
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
}
