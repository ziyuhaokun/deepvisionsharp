using DeepVisionSharp.Application.Models;

namespace DeepVisionSharp.Application.Training;

/// <summary>
/// 训练任务的默认命名：「模型 · 月-日 时:分:秒」，例如 <c>ResNet-34 · 11-23 13:20:05</c>。
/// 新建任务和复制任务共用这一套，复制时不沿用旧名字，也就不会出现「名字 (2) (2)」越叠越长。
/// </summary>
public static class TrainingJobNaming
{
    public static string Build(string? modelType, DateTime localNow, IEnumerable<string> existingNames)
    {
        var existing = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);
        var baseName = $"{ModelTypeNames.GetDisplayLabel(modelType)} · {localNow:MM-dd HH:mm:ss}";
        if (!existing.Contains(baseName))
        {
            return baseName;
        }

        // 同一秒内连点两次才会撞名；后缀只加在刚生成的名字上，不会叠加。
        for (var index = 2; index < 10_000; index++)
        {
            var candidate = $"{baseName} ({index})";
            if (!existing.Contains(candidate))
            {
                return candidate;
            }
        }

        return $"{baseName} ({Guid.NewGuid():N})";
    }
}
