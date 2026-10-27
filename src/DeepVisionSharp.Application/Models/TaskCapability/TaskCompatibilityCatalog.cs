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

    public static Result Validate(ProjectWorkflowKind workflow, ModelFamily family) =>
        workflow == ProjectWorkflowKind.Classification && family == ModelFamily.Classification
            ? Result.Success()
            : Result.Failure("图像分类项目仅支持分类模型。");
}
