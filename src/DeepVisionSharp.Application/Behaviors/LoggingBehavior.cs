using MediatR;
using Microsoft.Extensions.Logging;

namespace DeepVisionSharp.Application.Behaviors;

public sealed class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;

    public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    {
        _logger = logger;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!_logger.IsEnabled(LogLevel.Debug))
        {
            return await next();
        }

        var requestName = typeof(TRequest).Name;
        _logger.LogDebug("处理请求: {RequestName} {@Request}", requestName, request);

        var response = await next();
        _logger.LogDebug("请求完成: {RequestName} {@Response}", requestName, response);
        return response;
    }
}
