using DeepVisionSharp.Domain.Common;

namespace DeepVisionSharp.Application.Ports;

public interface ITrainingWorkspacePlanner
{
    /// <summary>
    /// 为一次全新训练创建独立 run 目录。版本号包含任务 Id，确保两个任务
    /// 永远不共享 OutputRoot（同一秒、同名模型也不会撞目录）。
    /// </summary>
    TrainingWorkspacePlan CreateRunPlan(Guid projectId, Guid jobId, string modelName);

    /// <summary>
    /// 续训复用已有 run 目录与日志，禁止再开时间戳目录。
    /// </summary>
    Result<TrainingWorkspacePlan> ReuseExistingRun(string outputRoot, string logFile);
}

public sealed record TrainingWorkspacePlan(
    string RunsRoot,
    string Version,
    string OutputRoot,
    string LogFile,
    string VocRoot);
