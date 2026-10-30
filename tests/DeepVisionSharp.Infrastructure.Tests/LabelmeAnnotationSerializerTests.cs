using Xunit;
using DeepVisionSharp.Domain.ValueObjects;
using DeepVisionSharp.Infrastructure.Services;

namespace DeepVisionSharp.Infrastructure.Tests;

public sealed class LabelmeAnnotationSerializerTests
{
    [Fact]
    public void Detection_shapes_round_trip_and_feed_the_label_index()
    {
        var document = new LabelmeDocument
        {
            Version = "5.0.1",
            ImagePath = "sample.jpg",
            ImageWidth = 1920,
            ImageHeight = 1080,
            Flags = LabelmeAnnotationSerializer.BuildDocumentFlags(imageClass: null, reviewed: true),
            Shapes = LabelmeAnnotationSerializer.ToLabelmeShapes(
            [
                new AnnotationShape("划痕", AnnotationShapeKind.Rectangle, 10, 20, 30, 40),
                new AnnotationShape("斑点", AnnotationShapeKind.Rectangle, 100, 200, 50, 60)
            ])
        };

        var json = LabelmeAnnotationSerializer.Serialize(document);
        var parsed = LabelmeAnnotationSerializer.Deserialize(json);

        Assert.True(LabelmeAnnotationSerializer.ReadReviewed(parsed!.Flags));
        var shapes = LabelmeAnnotationSerializer.ReadShapes(parsed);
        Assert.Equal(2, shapes.Count);
        Assert.Equal(10, shapes[0].X);
        Assert.Equal(40, shapes[0].Right);
        Assert.Equal(60, shapes[0].Bottom);

        var labels = AnnotationDocumentLabelReader.ReadLabels(json);
        Assert.Equal(["划痕", "斑点"], labels);
        Assert.True(AnnotationDocumentLabelReader.ReadReviewed(json));
        Assert.Null(AnnotationDocumentLabelReader.ReadImageClass(json));
    }

    [Fact]
    public void Legacy_classification_document_still_reads_as_a_single_class()
    {
        const string json = """
            {
              "version": "5.0.1",
              "flags": { "imageClass": "OK" },
              "imagePath": "sample.jpg",
              "imageWidth": 640,
              "imageHeight": 480
            }
            """;

        var parsed = LabelmeAnnotationSerializer.Deserialize(json);

        Assert.Equal("OK", LabelmeAnnotationSerializer.ReadImageClass(parsed!.Flags));
        Assert.Empty(LabelmeAnnotationSerializer.ReadShapes(parsed));
        Assert.False(LabelmeAnnotationSerializer.ReadReviewed(parsed.Flags));
        Assert.Equal(["OK"], AnnotationDocumentLabelReader.ReadLabels(json));
    }

    [Fact]
    public void Serialized_shape_uses_the_labelme_rectangle_contract()
    {
        var document = new LabelmeDocument
        {
            ImagePath = "sample.jpg",
            ImageWidth = 100,
            ImageHeight = 100,
            Flags = LabelmeAnnotationSerializer.BuildDocumentFlags("OK"),
            Shapes = LabelmeAnnotationSerializer.ToLabelmeShapes(
                [new AnnotationShape("划痕", AnnotationShapeKind.Rectangle, 1, 2, 3, 4)])
        };

        var json = LabelmeAnnotationSerializer.Serialize(document);

        Assert.Contains("\"shapes\"", json);
        Assert.Contains("\"shape_type\": \"rectangle\"", json);
        Assert.Contains("\"points\"", json);
        Assert.DoesNotContain("dvReviewed", json);
    }

    [Fact]
    public void Degenerate_shapes_are_dropped_on_save_and_load()
    {
        var json = LabelmeAnnotationSerializer.Serialize(new LabelmeDocument
        {
            Flags = LabelmeAnnotationSerializer.BuildDocumentFlags("OK"),
            Shapes = LabelmeAnnotationSerializer.ToLabelmeShapes(
                [
                    new AnnotationShape("划痕", AnnotationShapeKind.Rectangle, 10, 10, 0, 20),
                    new AnnotationShape("斑点", AnnotationShapeKind.Rectangle, 10, 10, 20, 20)
                ])
        });

        var shapes = LabelmeAnnotationSerializer.ReadShapes(LabelmeAnnotationSerializer.Deserialize(json));

        Assert.Single(shapes);
        Assert.Equal("斑点", shapes[0].Label);
    }
}
