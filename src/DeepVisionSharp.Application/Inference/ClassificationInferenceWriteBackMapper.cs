using DeepVisionSharp.Application.Projects;

namespace DeepVisionSharp.Application.Inference;

/// <summary>Shared classification write-back helpers for labeling and gallery batch inference.</summary>
public static class ClassificationInferenceWriteBackMapper
{
    public static IReadOnlyDictionary<string, ProjectLabelOption> BuildLabelLookup(
        IEnumerable<ProjectLabelOption> labels) =>
        labels
            .GroupBy(item => item.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
}
