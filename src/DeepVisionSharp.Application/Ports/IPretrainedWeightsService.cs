namespace DeepVisionSharp.Application.Ports;

/// <summary>
/// 提供训练用预训练权重的本地状态与下载。
/// </summary>
public interface IPretrainedWeightsService
{
    PretrainedWeightsStatus GetStatus(string modelType);

    /// <summary>下载并转换；<paramref name="progress"/> 为 0–1。失败抛异常，取消抛 <see cref="OperationCanceledException"/>。</summary>
    Task DownloadAsync(string modelType, IProgress<double>? progress, CancellationToken cancellationToken);
}

/// <param name="Supported">该模型能在软件里下载预训练权重。</param>
/// <param name="Available">本机已经有可用的预训练权重。</param>
public sealed record PretrainedWeightsStatus(
    bool Supported, bool Available, string DisplayName, int ApproxMegabytes, bool UsesFallback = false);
