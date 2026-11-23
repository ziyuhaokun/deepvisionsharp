using DeepVisionSharp.Application.Models;
using DeepVisionSharp.Application.Training;
using Xunit;

namespace DeepVisionSharp.Application.Tests.Training;

public sealed class TrainingJobNamingTests
{
    private static readonly DateTime Now = new(2026, 11, 23, 13, 20, 5);

    [Fact]
    public void Name_is_model_label_plus_time()
    {
        var name = TrainingJobNaming.Build(ModelTypeNames.Default, Now, []);

        Assert.Equal($"{ModelTypeNames.GetDisplayLabel(ModelTypeNames.Default)} · 11-23 13:20:05", name);
    }

    [Fact]
    public void Same_second_collision_adds_one_suffix_to_the_fresh_name()
    {
        var first = TrainingJobNaming.Build(ModelTypeNames.Default, Now, []);
        var second = TrainingJobNaming.Build(ModelTypeNames.Default, Now, [first]);
        var third = TrainingJobNaming.Build(ModelTypeNames.Default, Now, [first, second]);

        Assert.Equal($"{first} (2)", second);
        Assert.Equal($"{first} (3)", third);
    }

    [Fact]
    public void Copying_never_stacks_suffixes_because_the_name_is_rebuilt()
    {
        // 来源任务已经叫「xxx (2) (2)」也无所谓：新名字只看模型和时间。
        var name = TrainingJobNaming.Build(
            ModelTypeNames.Default,
            Now.AddMinutes(1),
            ["2026-11-21 10:01:03 (2) (2)"]);

        Assert.DoesNotContain("(", name);
    }

    [Fact]
    public void Collision_check_ignores_case()
    {
        var first = TrainingJobNaming.Build(ModelTypeNames.Default, Now, []);

        var second = TrainingJobNaming.Build(ModelTypeNames.Default, Now, [first.ToUpperInvariant()]);

        Assert.EndsWith("(2)", second);
    }
}
