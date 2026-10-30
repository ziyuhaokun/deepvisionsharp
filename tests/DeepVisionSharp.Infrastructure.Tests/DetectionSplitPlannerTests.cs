using Xunit;
using DeepVisionSharp.Infrastructure.Services;

namespace DeepVisionSharp.Infrastructure.Tests;

public sealed class DetectionSplitPlannerTests
{
    [Fact]
    public void Split_is_deterministic_and_keeps_both_sides_non_empty()
    {
        var samples = Enumerable.Range(0, 10)
            .Select(index => (Guid.Parse("11111111-1111-1111-1111-111111111111"), $"sample_{index:D2}.png"))
            .ToArray();

        var first = DetectionSplitPlanner.AssignValidationByRank(samples, 0.2);
        var second = DetectionSplitPlanner.AssignValidationByRank(samples, 0.2);

        Assert.True(first.IsSuccess);
        Assert.Equal(first.Value, second.Value);
        Assert.Equal(2, first.Value!.Count(value => value));
    }

    [Fact]
    public void Split_does_not_group_by_class_so_negatives_are_included()
    {
        var samples = new (Guid, string)[]
        {
            (Guid.NewGuid(), "a.png"),
            (Guid.NewGuid(), "b.png"),
            (Guid.NewGuid(), "c.png")
        };

        var split = DetectionSplitPlanner.AssignValidationByRank(samples, 0.34);

        Assert.True(split.IsSuccess);
        Assert.Equal(3, split.Value!.Length);
        Assert.Contains(true, split.Value);
        Assert.Contains(false, split.Value);
    }

    [Fact]
    public void One_sample_cannot_be_split()
    {
        var split = DetectionSplitPlanner.AssignValidationByRank(
            [(Guid.NewGuid(), "only.png")],
            0.2);

        Assert.True(split.IsFailure);
    }

    [Fact]
    public void Adding_one_sample_moves_at_most_one_membership()
    {
        var samples = Enumerable.Range(0, 20)
            .Select(index => (Guid.NewGuid(), $"image_{index:D2}.png"))
            .ToList();
        var before = DetectionSplitPlanner.AssignValidationByRank(samples, 0.25).Value!;

        samples.Add((Guid.NewGuid(), "new_image.png"));
        var after = DetectionSplitPlanner.AssignValidationByRank(samples, 0.25).Value!;

        var changed = 0;
        for (var index = 0; index < before.Length; index++)
        {
            if (before[index] != after[index])
            {
                changed++;
            }
        }

        Assert.True(changed <= 2, $"划分变动了 {changed} 个样本，哈希排名应最多移动边界附近的一两个。");
    }
}
