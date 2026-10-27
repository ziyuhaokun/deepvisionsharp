using Microsoft.Extensions.Logging;

namespace DeepVisionSharp.Shell.Services;

/// <summary>
/// 在同步回调中安全启动后台异步工作，避免未观察的 Task 异常。
/// </summary>
public static class ShellAsync
{
    /// <summary>
    /// 启动并观察 <paramref name="work"/>。异常会记日志，可选回调（如 snackbar）。
    /// 取消类异常默认静默。
    /// </summary>
    public static void FireAndForget(
        Func<Task> work,
        ILogger logger,
        string operation,
        Action<Exception>? onError = null)
    {
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);

        _ = RunObservedAsync(work, logger, operation, onError);
    }

    /// <summary>
    /// 同 <see cref="FireAndForget"/>，返回可被测试或上层可选 await 的 Task（仍已在内部捕获异常）。
    /// </summary>
    public static Task RunObservedAsync(
        Func<Task> work,
        ILogger logger,
        string operation,
        Action<Exception>? onError = null)
    {
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);

        return ExecuteAsync(work, logger, operation, onError);
    }

    private static async Task ExecuteAsync(
        Func<Task> work,
        ILogger logger,
        string operation,
        Action<Exception>? onError)
    {
        try
        {
            await work().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // 调用方取消：不记错误、不打扰用户。
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Background operation failed: {Operation}", operation);
            try
            {
                onError?.Invoke(ex);
            }
            catch (Exception callbackEx)
            {
                logger.LogError(
                    callbackEx,
                    "Error callback failed after background operation {Operation}",
                    operation);
            }
        }
    }
}
