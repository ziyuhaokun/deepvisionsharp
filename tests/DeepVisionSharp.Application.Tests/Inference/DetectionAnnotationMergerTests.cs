using Xunit;
using DeepVisionSharp.Application.Inference;
using DeepVisionSharp.Domain.ValueObjects;

namespace DeepVisionSharp.Application.Tests.Inference;

public sealed class DetectionAnnotationMergerTests
{
    private static readonly string[] Labels = ["scratch", "spot"];

    [Fact]
    public void Replace_overwrites_the_box_list_and_marks_the_sample_reviewed()
    {
        var existing = new AnnotationDocument(
            100, 100, null,
            [new AnnotationShape("spot", AnnotationShapeKind.Rectangle, 1, 1, 10, 10)], true);

        var result = DetectionAnnotationMerger.Merge(
            existing,
            [Box("scratch", 20, 20, 30, 30)],
            Labels,
            append: false,
            100,
            100);

        Assert.True(result.IsSuccess);
        var document = result.Value!;
        var shape = Assert.Single(document.Boxes);
        Assert.Equal("scratch", shape.Label);
        Assert.True(document.IsReviewed);
    }

    [Fact]
    public void Append_keeps_existing_boxes_and_skips_near_duplicates()
    {
        var existing = new AnnotationDocument(
            100, 100, null,
            [new AnnotationShape("scratch", AnnotationShapeKind.Rectangle, 10, 10, 20, 20)], true);

        var result = DetectionAnnotationMerger.Merge(
            existing,
            [
                Box("scratch", 10.5, 10.5, 20, 20),
                Box("scratch", 60, 60, 20, 20)
            ],
            Labels,
            append: true,
            100,
            100);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Boxes.Count);
    }

    [Fact]
    public void Unknown_classes_are_ignored_and_empty_predictions_leave_the_sample_alone()
    {
        var existing = new AnnotationDocument(100, 100);

        var result = DetectionAnnotationMerger.Merge(
            existing,
            [Box("unknown", 1, 1, 5, 5)],
            Labels,
            append: true,
            100,
            100);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
    }

    [Fact]
    public void Missing_image_size_is_rejected()
    {
        var result = DetectionAnnotationMerger.Merge(
            new AnnotationDocument(0, 0),
            [Box("scratch", 1, 1, 5, 5)],
            Labels,
            append: true,
            0,
            0);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Boxes_outside_the_image_are_clipped()
    {
        var result = DetectionAnnotationMerger.Merge(
            new AnnotationDocument(100, 100),
            [Box("scratch", 90, 90, 40, 40)],
            Labels,
            append: false,
            100,
            100);

        var shape = Assert.Single(result.Value!.Boxes);
        Assert.True(shape.Right <= 100);
        Assert.True(shape.Bottom <= 100);
    }

    private static DetectionBoxResult Box(string label, double x, double y, double width, double height) =>
        new()
        {
            Label = label,
            ClassName = label,
            Confidence = 0.9,
            X = x,
            Y = y,
            Width = width,
            Height = height
        };
}
