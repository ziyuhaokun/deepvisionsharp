namespace DeepVisionSharp.Domain.ValueObjects;

public sealed class ProjectTypeUnsupportedException(string projectType) : InvalidOperationException(
    $"不支持的项目类型：{projectType}。当前仅支持图像分类。")
{
    public string ProjectType { get; } = projectType;
}
