using DeepVisionSharp.Application.Models;
using DeepVisionSharp.TorchSharp.Device;

namespace DeepVisionSharp.Infrastructure.Services;

/// <summary>
/// TorchSharp 分类训练环境检查：不依赖 Python；CUDA 可用性记为 Info。
/// </summary>
internal sealed class TorchSharpTrainingEnvironmentChecker : ITrainingEnvironmentChecker
{
    public string CheckerKey => BackendKeys.TorchSharpClassification;

    public bool CanHandle(TrainingEnvironmentCheckContext context)
        => context.Backend.BackendKey is BackendKeys.TorchSharpClassification
            or BackendKeys.TorchSharpDetection;

    public Task<TrainingEnvironmentCheckResult> CheckAsync(
        TrainingEnvironmentCheckContext context,
        CancellationToken cancellationToken)
    {
        var notes = new List<TrainingEnvironmentIssue>();
        if (TorchSharpDeviceManager.IsCudaAvailable)
        {
            notes.Add(new TrainingEnvironmentIssue(
                "TORCHSHARP_CUDA",
                "已检测到 CUDA，TorchSharp 训练可使用 GPU",
                Severity: TrainingEnvironmentIssueSeverity.Info));
        }
        else
        {
            notes.Add(new TrainingEnvironmentIssue(
                "TORCHSHARP_CPU",
                "未检测到 CUDA，TorchSharp 训练将使用 CPU",
                "如需 GPU，请确认已安装 TorchSharp-cuda-windows 与 NVIDIA 驱动",
                TrainingEnvironmentIssueSeverity.Info));
        }

        return Task.FromResult(new TrainingEnvironmentCheckResult([], [], notes));
    }
}
