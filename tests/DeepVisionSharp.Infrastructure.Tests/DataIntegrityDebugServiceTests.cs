using System.Data.SQLite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using DeepVisionSharp.Application.Galleries;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Domain.ValueObjects;
using DeepVisionSharp.Infrastructure.Modules;
using DeepVisionSharp.Infrastructure.Persistence;
using DeepVisionSharp.Infrastructure.Services;

namespace DeepVisionSharp.Infrastructure.Tests;

public sealed class DataIntegrityDebugServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        FindRepositoryTemp(), "integrity-" + Guid.NewGuid().ToString("N"));

    public DataIntegrityDebugServiceTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            SQLiteConnection.ClearAllPools();
        }
        catch
        {
        }

        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (Directory.Exists(_directory))
                {
                    Directory.Delete(_directory, recursive: true);
                }

                return;
            }
            catch (IOException) when (attempt < 4)
            {
                Thread.Sleep(50);
            }
            catch (UnauthorizedAccessException) when (attempt < 4)
            {
                Thread.Sleep(50);
            }
        }
    }

    [Fact]
    public async Task CheckAndRepairAsync_preserves_detection_boxes_and_reviewed_negatives()
    {
        using var services = new ServiceCollection()
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddSingleton<IDataRootProvider>(new DefaultDataRootProvider(Path.Combine(_directory, "data")))
            .AddDataModule()
            .BuildServiceProvider();

        using var db = services.GetRequiredService<SqliteDb>().Instance;
        var fileStore = services.GetRequiredService<IProjectFileStore>();
        var service = services.GetRequiredService<IDataIntegrityDebugService>();

        var projectId = Guid.NewGuid();
        var galleryId = Guid.NewGuid();
        await db.Insert(new SqlProject
        {
            Id = projectId,
            Name = "检测完整性测试",
            Type = ProjectTypes.Detection,
            CreatedUtc = DateTime.UtcNow,
            LastUpdatedUtc = DateTime.UtcNow
        }).ExecuteAffrowsAsync();

        await db.Insert(new SqlGallery
        {
            Id = galleryId,
            ProjectId = projectId,
            Name = "测试图库",
            SampleCount = 5,
            AnnotatedCount = 0,
            LastUpdatedUtc = DateTime.UtcNow
        }).ExecuteAffrowsAsync();

        var sampleFiles = new[]
        {
            "boxed.jpg",
            "negative.jpg",
            "empty_unreviewed.jpg",
            "corrupt.jpg",
            "unlabeled.jpg"
        };

        foreach (var fileName in sampleFiles)
        {
            await db.Insert(new SqlSample
            {
                Id = Guid.NewGuid(),
                GalleryId = galleryId,
                FileName = fileName,
                IsAnnotated = false
            }).ExecuteAffrowsAsync();
        }

        var imagesDir = fileStore.GetGalleryImagesDirectory(projectId, galleryId, ensureExists: true);
        foreach (var fileName in sampleFiles)
        {
            await File.WriteAllBytesAsync(Path.Combine(imagesDir, fileName), [1, 2, 3]);
        }

        // Unreviewed sample with detection box: tests ReadLabels independently of dvReviewed.
        var boxedDoc = new LabelmeDocument
        {
            Version = "5.0.1",
            ImagePath = "boxed.jpg",
            ImageWidth = 100,
            ImageHeight = 100,
            Flags = LabelmeAnnotationSerializer.BuildDocumentFlags(null, reviewed: false),
            Shapes = LabelmeAnnotationSerializer.ToLabelmeShapes(
            [
                new AnnotationShape("scratch", AnnotationShapeKind.Rectangle, 10, 10, 20, 20)
            ])
        };
        await File.WriteAllTextAsync(
            SampleAnnotationFiles.GetPath(Path.Combine(imagesDir, "boxed.jpg")),
            LabelmeAnnotationSerializer.Serialize(boxedDoc));

        // Reviewed negative sample with zero shapes: tests ReadReviewed independently of ReadLabels.
        var negativeDoc = new LabelmeDocument
        {
            Version = "5.0.1",
            ImagePath = "negative.jpg",
            ImageWidth = 100,
            ImageHeight = 100,
            Flags = LabelmeAnnotationSerializer.BuildDocumentFlags(null, reviewed: true),
            Shapes = []
        };
        await File.WriteAllTextAsync(
            SampleAnnotationFiles.GetPath(Path.Combine(imagesDir, "negative.jpg")),
            LabelmeAnnotationSerializer.Serialize(negativeDoc));

        // Unreviewed empty sidecar must NOT count as annotated.
        var emptyUnreviewedDoc = new LabelmeDocument
        {
            Version = "5.0.1",
            ImagePath = "empty_unreviewed.jpg",
            ImageWidth = 100,
            ImageHeight = 100,
            Flags = LabelmeAnnotationSerializer.BuildDocumentFlags(null, reviewed: false),
            Shapes = []
        };
        await File.WriteAllTextAsync(
            SampleAnnotationFiles.GetPath(Path.Combine(imagesDir, "empty_unreviewed.jpg")),
            LabelmeAnnotationSerializer.Serialize(emptyUnreviewedDoc));

        // Corrupt JSON sidecar must NOT count as annotated.
        await File.WriteAllTextAsync(
            SampleAnnotationFiles.GetPath(Path.Combine(imagesDir, "corrupt.jpg")),
            "{not valid json");

        var result = await service.CheckAndRepairAsync();
        Assert.True(result.IsSuccess, result.Error);

        var samples = await db.Select<SqlSample>()
            .Where(row => row.GalleryId == galleryId)
            .ToListAsync();
        Assert.True(samples.Single(s => s.FileName == "boxed.jpg").IsAnnotated);
        Assert.True(samples.Single(s => s.FileName == "negative.jpg").IsAnnotated);
        Assert.False(samples.Single(s => s.FileName == "empty_unreviewed.jpg").IsAnnotated);
        Assert.False(samples.Single(s => s.FileName == "corrupt.jpg").IsAnnotated);
        Assert.False(samples.Single(s => s.FileName == "unlabeled.jpg").IsAnnotated);

        var gallery = await db.Select<SqlGallery>().Where(row => row.Id == galleryId).FirstAsync();
        Assert.Equal(2, gallery.AnnotatedCount);

        var labels = await db.Select<SqlSampleLabelIndex>()
            .Where(row => row.GalleryId == galleryId && row.FileName == "boxed.jpg")
            .ToListAsync();
        Assert.Contains(labels, item => item.Label == "scratch");
    }

    [Fact]
    public async Task SaveBatchAsync_deduplicates_duplicate_filenames_and_rolls_back_on_failure()
    {
        using var services = new ServiceCollection()
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddSingleton<IDataRootProvider>(new DefaultDataRootProvider(Path.Combine(_directory, "data-batch")))
            .AddDataModule()
            .BuildServiceProvider();

        using var db = services.GetRequiredService<SqliteDb>().Instance;
        var fileStore = services.GetRequiredService<IProjectFileStore>();
        var workflow = services.GetRequiredService<IAnnotationWorkflowService>();

        var projectId = Guid.NewGuid();
        var galleryId = Guid.NewGuid();
        await db.Insert(new SqlProject
        {
            Id = projectId,
            Name = "批量保存测试",
            Type = ProjectTypes.Detection,
            CreatedUtc = DateTime.UtcNow,
            LastUpdatedUtc = DateTime.UtcNow
        }).ExecuteAffrowsAsync();

        await db.Insert(new SqlGallery
        {
            Id = galleryId,
            ProjectId = projectId,
            Name = "批量图库",
            SampleCount = 2,
            AnnotatedCount = 0,
            LastUpdatedUtc = DateTime.UtcNow
        }).ExecuteAffrowsAsync();

        foreach (var fileName in new[] { "a.jpg", "b.jpg" })
        {
            await db.Insert(new SqlSample
            {
                Id = Guid.NewGuid(),
                GalleryId = galleryId,
                FileName = fileName,
                IsAnnotated = false
            }).ExecuteAffrowsAsync();
        }

        var imagesDir = fileStore.GetGalleryImagesDirectory(projectId, galleryId, ensureExists: true);
        var imageA = Path.Combine(imagesDir, "a.jpg");
        var imageB = Path.Combine(imagesDir, "b.jpg");
        await File.WriteAllBytesAsync(imageA, [1, 2, 3]);
        await File.WriteAllBytesAsync(imageB, [1, 2, 3]);

        // Duplicate entries for a.jpg in one batch: last item wins without File.Move collision.
        await workflow.SaveBatchAsync(
            galleryId,
            [
                new AnnotationSaveItem("a.jpg", 100, 100, null, [new AnnotationShape("old", AnnotationShapeKind.Rectangle, 1, 1, 10, 10)], IsReviewed: true),
                new AnnotationSaveItem("a.jpg", 100, 100, null, [new AnnotationShape("final", AnnotationShapeKind.Rectangle, 2, 2, 20, 20)], IsReviewed: true)
            ]);

        var sidecarA = SampleAnnotationFiles.GetPath(imageA);
        var sidecarB = SampleAnnotationFiles.GetPath(imageB);
        Assert.True(File.Exists(sidecarA));
        var originalJsonA = await File.ReadAllTextAsync(sidecarA);
        Assert.Contains("final", originalJsonA);

        // Lock b.jpg's target sidecar path as a directory so the second promotion throws IOException
        // after a.jpg was already replaced via File.Replace, exercising disk + DB rollback.
        Directory.CreateDirectory(sidecarB);
        try
        {
            await Assert.ThrowsAnyAsync<Exception>(() =>
                workflow.SaveBatchAsync(
                    galleryId,
                    [
                        new AnnotationSaveItem("a.jpg", 100, 100, null, [new AnnotationShape("corrupted", AnnotationShapeKind.Rectangle, 5, 5, 10, 10)], IsReviewed: true),
                        new AnnotationSaveItem("b.jpg", 100, 100, null, [new AnnotationShape("new", AnnotationShapeKind.Rectangle, 5, 5, 10, 10)], IsReviewed: true)
                    ]));
        }
        finally
        {
            Directory.Delete(sidecarB);
        }

        // a.jpg's sidecar and DB row must both be rolled back to "final".
        var restoredJsonA = await File.ReadAllTextAsync(sidecarA);
        Assert.Equal(originalJsonA, restoredJsonA);
        var dbRowA = await db.Select<SqlAnnotation>()
            .Where(row => row.GalleryId == galleryId && row.FileName == "a.jpg")
            .FirstAsync();
        Assert.Contains("final", dbRowA.DocumentJson);
        Assert.DoesNotContain("corrupted", dbRowA.DocumentJson);
    }

    private static string FindRepositoryTemp()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "DeepVisionSharp.slnx")))
            {
                var temp = Path.Combine(current.FullName, "temp");
                Directory.CreateDirectory(temp);
                return temp;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("未找到仓库根目录，无法放置测试产物。");
    }
}
