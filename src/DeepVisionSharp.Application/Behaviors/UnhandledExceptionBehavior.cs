using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace DeepVisionSharp.Application.Behaviors;

public sealed class UnhandledExceptionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ILogger<UnhandledExceptionBehavior<TRequest, TResponse>> _logger;

    public UnhandledExceptionBehavior(ILogger<UnhandledExceptionBehavior<TRequest, TResponse>> logger)
    {
        _logger = logger;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        try
        {
            return await next();
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning(ex, "输入验证失败：{RequestName}", typeof(TRequest).Name);
            return MapOrRethrow($"输入验证失败：{ex.Message}", ex);
        }
        catch (DeepVisionSharp.Domain.ValueObjects.ProjectTypeUnsupportedException ex)
        {
            _logger.LogWarning("归档项目操作已拒绝：{RequestName}", typeof(TRequest).Name);
            return MapOrRethrow(ex.Message, ex);
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "文件操作失败：{RequestName}", typeof(TRequest).Name);
            return MapOrRethrow("文件操作失败，请检查磁盘空间和权限", ex);
        }
        catch (TimeoutException ex)
        {
            _logger.LogError(ex, "操作超时：{RequestName}", typeof(TRequest).Name);
            return MapOrRethrow("操作超时，请稍后重试", ex);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogInformation(ex, "操作已取消：{RequestName}", typeof(TRequest).Name);
            return MapOrRethrow("操作已取消", ex);
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "未知错误：{RequestName}", typeof(TRequest).Name);
            return MapOrRethrow("系统错误，请联系管理员", ex);
        }
    }

    private static TResponse MapOrRethrow(string errorMessage, Exception original)
    {
        if (ResultFailureFactory.TryCreateFailureResponse<TResponse>(errorMessage, out var failure))
        {
            return failure;
        }

        // Non-Result responses (lists, bool, entities, int, …) must not become default/null.
        ExceptionDispatchRethrow(original);
        return default!; // unreachable
    }

    private static void ExceptionDispatchRethrow(Exception exception)
    {
        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception).Throw();
    }
}
