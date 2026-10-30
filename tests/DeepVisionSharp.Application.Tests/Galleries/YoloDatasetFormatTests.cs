using Xunit;
using DeepVisionSharp.Domain.Datasets;
using DeepVisionSharp.Domain.ValueObjects;

namespace DeepVisionSharp.Application.Tests.Galleries;

public sealed class YoloDatasetFormatTests
{
    [Fact]
    public void Parses_plain_classes_file()
    {
        var result = YoloDatasetFormat.ParseClasses("OK\n划痕\n斑点\n");

        Assert.True(result.IsSuccess);
        Assert.Equal(["OK", "划痕", "斑点"], result.Value);
    }

    [Fact]
    public void Parses_inline_yaml_names()
    {
        var result = YoloDatasetFormat.ParseClasses("path: ./\nnc: 2\nnames: [scratch, spot]\n");

        Assert.True(result.IsSuccess);
        Assert.Equal(["scratch", "spot"], result.Value);
    }

    [Fact]
    public void Parses_yaml_names_block_with_indices()
    {
        const string yaml = """
            train: images/train
            val: images/val
            nc: 2
            names:
              0: scratch
              1: spot
            """;

        var result = YoloDatasetFormat.ParseClasses(yaml);

        Assert.True(result.IsSuccess);
        Assert.Equal(["scratch", "spot"], result.Value);
    }

    [Fact]
    public void Parses_yaml_names_block_with_dashes()
    {
        const string yaml = "names:\n  - scratch\n  - spot\n";

        var result = YoloDatasetFormat.ParseClasses(yaml);

        Assert.True(result.IsSuccess);
        Assert.Equal(["scratch", "spot"], result.Value);
    }

    [Fact]
    public void Parses_label_lines_and_reports_bad_rows()
    {
        const string labels = """
            0 0.5 0.5 0.25 0.25
            1 0.1 0.2 0.05 0.1
            2 0.5 0.5 0.1 0.1
            not a label line
            """;

        var boxes = YoloDatasetFormat.ParseLabels(labels, classCount: 2, out var warnings);

        Assert.Equal(2, boxes.Count);
        Assert.Equal(2, warnings.Count);
        Assert.Equal(1, boxes[1].ClassIndex);
    }

    [Fact]
    public void Shape_round_trips_through_yolo_text()
    {
        var shape = new AnnotationShape("划痕", AnnotationShapeKind.Rectangle, 100, 50, 200, 100);

        var text = YoloDatasetFormat.FormatLabels([(0, shape)], imageWidth: 1000, imageHeight: 500);

        var boxes = YoloDatasetFormat.ParseLabels(text, classCount: 1, out var warnings);
        Assert.Empty(warnings);
        var restored = YoloDatasetFormat.ToShape(boxes[0], "划痕", 1000, 500);

        Assert.NotNull(restored);
        Assert.Equal(100, restored!.X, precision: 3);
        Assert.Equal(50, restored.Y, precision: 3);
        Assert.Equal(200, restored.Width, precision: 3);
        Assert.Equal(100, restored.Height, precision: 3);
    }

    [Fact]
    public void Box_outside_the_image_is_clipped()
    {
        var box = new YoloBox(0, 0.95, 0.95, 0.2, 0.2);

        var shape = YoloDatasetFormat.ToShape(box, "spot", imageWidth: 100, imageHeight: 100);

        Assert.NotNull(shape);
        Assert.True(shape!.Right <= 100);
        Assert.True(shape.Bottom <= 100);
        Assert.True(shape.Width > 0);
    }

    [Fact]
    public void Empty_label_file_is_a_negative_sample()
    {
        var boxes = YoloDatasetFormat.ParseLabels(string.Empty, classCount: 1, out var warnings);

        Assert.Empty(boxes);
        Assert.Empty(warnings);
        Assert.Equal(string.Empty, YoloDatasetFormat.FormatLabels([], 100, 100));
    }
}
