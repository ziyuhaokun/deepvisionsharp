using DeepVisionSharp.Domain.Common;
using DeepVisionSharp.Domain.ValueObjects;

namespace DeepVisionSharp.Application.Models;

public static class TaskCompatibilityCatalog
{
    public static Result Validate(ProjectWorkflowKind workflow, string? modelType)
    {
        if (string.IsNullOrWhiteSpace(modelType))
        {
            return Result.Failure("模型类型不能为空。");
        }

        if (!ModelTypeNames.TryNormalize(modelType, out var normalized)
            || !ModelCapabilityCatalog.TryResolve(normalized, out var capabilities))
        {
            return Result.Failure($"不支持的模型类型：{modelType.Trim()}");
        }

        return Validate(workflow, capabilities.Family);
    }

    public static Result Validate(ProjectWorkflowKind workflow, ModelFamily family)
    {
        var expected = workflow switch
        {
            ProjectWorkflowKind.Classification => ModelFamily.Classification,
            ProjectWorkflowKind.Detection => ModelFamily.Detection,
            _ => (ModelFamily?)null
        };

        if (expected is null)
        {
            return Result.Failure($"未知的项目工作流：{workflow}。");
        }

        return expected == family
            ? Result.Success()
            : Result.Failure(workflow == ProjectWorkflowKind.Detection
                ? "目标检测项目仅支持检测模型。"
                : "图像分类项目仅支持分类模型。");
    }
}
