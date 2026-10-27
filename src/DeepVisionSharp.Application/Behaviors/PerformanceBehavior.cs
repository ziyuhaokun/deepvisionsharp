using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace DeepVisionSharp.Application.Behaviors;

public sealed class PerformanceBehaviorOptions
{
    public int WarningThresholdMilliseconds { get; set; } = 500;
}

public sealed class PerformanceBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ILogger<PerformanceBehavior<TRequest, TResponse>> _logger;
    private readonly PerformanceBehaviorOptions _options;

    public PerformanceBehavior(
        ILogger<PerformanceBehavior<TRequest, TResponse>> logger,
        IOptions<PerformanceBehaviorOptions>? options = null)
    {
        _logger = logger;
        _options = options?.Value ?? new PerformanceBehaviorOptions();
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            return await next();
        }
        finally
        {
            stopwatch.Stop();
            LogDuration(stopwatch.ElapsedMilliseconds);
        }
    }

    private void LogDuration(long elapsedMilliseconds)
    {
        var requestName = typeof(TRequest).Name;
        var threshold = _options.WarningThresholdMilliseconds;

        if (elapsedMilliseconds >= threshold)
        {
            _logger.LogWarning(
                "慢请求: {RequestName} 耗时 {ElapsedMilliseconds}ms",
                requestName,
                elapsedMilliseconds);
            return;
        }

        _logger.LogDebug(
            "请求耗时: {RequestName} 耗时 {ElapsedMilliseconds}ms",
            requestName,
            elapsedMilliseconds);
    }
}
