using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using DeepVisionSharp.Application.Galleries;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Domain.Datasets;
using DeepVisionSharp.Domain.ValueObjects;
using DeepVisionSharp.Infrastructure.Modules;
using DeepVisionSharp.Infrastructure.Persistence;
using DeepVisionSharp.Infrastructure.Services;
using DeepVisionSharp.TorchSharp.Detection.Training;
using Xunit;

namespace DeepVisionSharp.Infrastructure.Tests;

public sealed class DetectionDatasetExportTests
{
    [Fact]
    public async Task Export_keeps_labels_distinct_for_images_with_the_same_stem()
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(repository.FullName, "DeepVisionSharp.slnx")))
        {
            repository = repository.Parent ?? throw new DirectoryNotFoundException("DeepVisionSharp.slnx");
        }

        var directory = Directory.CreateDirectory(Path.Combine(repository.FullName, "temp", $"detection-export-{Guid.NewGuid():N}"));
        try
        {
            using var services = new ServiceCollection()
                .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
                .AddSingleton<IDataRootProvider>(new DefaultDataRootProvider(Path.Combine(directory.FullName, "data")))
                .AddDataModule()
                .BuildServiceProvider();
            using var db = services.GetRequiredService<SqliteDb>().Instance;
            var store = services.GetRequiredService<IProjectFileStore>();
            var workflow = services.GetRequiredService<IAnnotationWorkflowService>();
            var exporter = services.GetRequiredService<IDetectionDatasetExportService>();
            var projectId = Guid.NewGuid();
            var galleryId = Guid.NewGuid();
            await db.Insert(new SqlProject
            {
                Id = projectId,
                Name = "Detection export",
                Type = ProjectTypes.Detection,
                CreatedUtc = DateTime.UtcNow,
                LastUpdatedUtc = DateTime.UtcNow
            }).ExecuteAffrowsAsync();
            await db.Insert(new SqlGallery
            {
                Id = galleryId,
                ProjectId = projectId,
                Name = "Samples",
                SampleCount = 2,
                LastUpdatedUtc = DateTime.UtcNow
            }).ExecuteAffrowsAsync();

            var images = store.GetGalleryImagesDirectory(projectId, galleryId, ensureExists: true);
            foreach (var (fileName, label, x) in new[] { ("part.jpg", "scratch", 10), ("part.png", "dent", 60) })
            {
                await db.Insert(new SqlSample { Id = Guid.NewGuid(), GalleryId = galleryId, FileName = fileName })
                    .ExecuteAffrowsAsync();
                // Only the exporter and dataset loader are exercised. Dimensions come from the annotation.
                await File.WriteAllBytesAsync(Path.Combine(images, fileName), [1]);
                await workflow.SaveAsync(galleryId, fileName, 100, 100, null,
                    [new AnnotationShape(label, AnnotationShapeKind.Rectangle, x, 10, 10, 10)], isReviewed: true);
            }

            var roles = new Dictionary<Guid, GalleryDatasetRole> { [galleryId] = GalleryDatasetRole.TrainingAndValidation };
            async Task<DatasetManifest> ExportAsync(string output, TrainingDatasetSplitMode splitMode)
            {
                var result = await exporter.ExportAsync(projectId, [galleryId], roles,
                    splitMode, 0.2, ["scratch", "dent"], output);
                Assert.True(result.IsSuccess, result.Error);
                var manifest = DatasetManifest.Read(Path.Combine(output, DatasetManifest.FileName), DatasetManifest.Detection);
                Assert.Equal(result.Value!.DatasetFingerprint, manifest.DatasetFingerprint);
                return manifest;
            }

            var output = Path.Combine(directory.FullName, "dataset");
            var first = await ExportAsync(output, TrainingDatasetSplitMode.PurposeAssignment);
            Assert.Equal(2, first.SchemaVersion);
            Assert.Equal(2, first.SplitOverlapCount);
            Assert.Equal("purpose-assignment", first.SplitMethod);
            Assert.Null(first.SplitSeed);
            Assert.Equal(new[] { "scratch", "dent" }, first.Classes);
            Assert.Equal(2, first.TrainingBoxes);
            Assert.Equal(2, first.ValidationBoxes);

            var dataset = DetectionFolderDataset.Load(output);
            Assert.Equal(first.DatasetFingerprint, dataset.Fingerprint);
            foreach (var split in new[] { dataset.Train, dataset.Val })
            {
                Assert.Equal(2, split.Count);
                Assert.Equal(2, split.Select(sample => sample.LabelPath).Distinct(StringComparer.OrdinalIgnoreCase).Count());
                foreach (var sample in split)
                {
                    var box = Assert.Single(YoloDatasetFormat.ParseLabels(await File.ReadAllTextAsync(sample.LabelPath), 2, out var warnings));
                    Assert.Empty(warnings);
                    var isJpeg = Path.GetExtension(sample.ImagePath) == ".jpg";
                    Assert.Equal(isJpeg ? 0 : 1, box.ClassIndex);
                    Assert.Equal(isJpeg ? 0.15 : 0.65, box.CenterX, precision: 5);
                    var relative = Path.GetRelativePath(output, sample.ImagePath).Replace(Path.DirectorySeparatorChar, '/');
                    var entry = Assert.Single(first.Samples!, entry => entry.ImagePath == relative);
                    Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(sample.LabelPath))),
                        entry.LabelSha256);
                }
            }

            var repeat = await ExportAsync(Path.Combine(directory.FullName, "same"), TrainingDatasetSplitMode.PurposeAssignment);
            Assert.Equal(first.DatasetFingerprint, repeat.DatasetFingerprint);
            Assert.Equal(first.Samples, repeat.Samples);

            await workflow.SaveAsync(galleryId, "part.jpg", 100, 100, null,
                [new AnnotationShape("scratch", AnnotationShapeKind.Rectangle, 20, 10, 10, 10)], isReviewed: true);
            var changed = await ExportAsync(Path.Combine(directory.FullName, "changed"), TrainingDatasetSplitMode.PurposeAssignment);
            Assert.NotEqual(first.DatasetFingerprint, changed.DatasetFingerprint);
            Assert.Equal(first.Samples!.Select(item => item.ImageSha256), changed.Samples!.Select(item => item.ImageSha256));

            var automatic = await ExportAsync(Path.Combine(directory.FullName, "auto"), TrainingDatasetSplitMode.AutoSplit);
            Assert.Equal("global-hash-rank-v1", automatic.SplitMethod);
            Assert.Equal(ClassificationSplitPlanner.Seed, automatic.SplitSeed);
            Assert.Equal(0, automatic.SplitOverlapCount);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
