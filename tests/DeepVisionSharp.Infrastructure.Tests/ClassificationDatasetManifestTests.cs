using System.Security.Cryptography;
using DeepVisionSharp.Application.Galleries;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Domain.Datasets;
using DeepVisionSharp.Domain.ValueObjects;
using DeepVisionSharp.Infrastructure.Modules;
using DeepVisionSharp.Infrastructure.Persistence;
using DeepVisionSharp.Infrastructure.Services;
using DeepVisionSharp.TorchSharp.Training;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DeepVisionSharp.Infrastructure.Tests;

public sealed class ClassificationDatasetManifestTests
{
    [Fact]
    public async Task Export_records_the_effective_split_ordered_classes_and_image_content()
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(repository.FullName, "DeepVisionSharp.slnx")))
        {
            repository = repository.Parent ?? throw new DirectoryNotFoundException("DeepVisionSharp.slnx");
        }

        var directory = Directory.CreateDirectory(Path.Combine(repository.FullName, "temp", $"classification-export-{Guid.NewGuid():N}"));
        try
        {
            using var services = new ServiceCollection()
                .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
                .AddSingleton<IDataRootProvider>(new DefaultDataRootProvider(Path.Combine(directory.FullName, "data")))
                .AddDataModule()
                .BuildServiceProvider();
            using var db = services.GetRequiredService<SqliteDb>().Instance;
            var store = services.GetRequiredService<IProjectFileStore>();
            var annotations = services.GetRequiredService<IAnnotationWorkflowService>();
            var exporter = services.GetRequiredService<IClassificationDatasetExportService>();
            var projectId = Guid.NewGuid();
            var galleryId = Guid.NewGuid();
            await db.Insert(new SqlProject
            {
                Id = projectId,
                Name = "Classification export",
                Type = ProjectTypes.Classification,
                CreatedUtc = DateTime.UtcNow,
                LastUpdatedUtc = DateTime.UtcNow
            }).ExecuteAffrowsAsync();
            await db.Insert(new SqlGallery
            {
                Id = galleryId,
                ProjectId = projectId,
                Name = "Samples",
                SampleCount = 4,
                LastUpdatedUtc = DateTime.UtcNow
            }).ExecuteAffrowsAsync();

            var images = store.GetGalleryImagesDirectory(projectId, galleryId, ensureExists: true);
            foreach (var (fileName, label) in new[] { ("a.png", "OK"), ("b.png", "OK"), ("c.png", "NG"), ("d.png", "NG") })
            {
                await db.Insert(new SqlSample { Id = Guid.NewGuid(), GalleryId = galleryId, FileName = fileName })
                    .ExecuteAffrowsAsync();
                await File.WriteAllBytesAsync(Path.Combine(images, fileName), [1, 2, 3]);
                await annotations.SaveAsync(galleryId, fileName, 100, 100, label);
            }

            var roles = new Dictionary<Guid, GalleryDatasetRole> { [galleryId] = GalleryDatasetRole.TrainingAndValidation };
            async Task<DatasetManifest> ExportAsync(string name, TrainingDatasetSplitMode splitMode, string[] classes)
            {
                var output = Path.Combine(directory.FullName, name);
                var result = await exporter.ExportAsync(projectId, [galleryId], roles, splitMode, 0.5, classes, output);
                Assert.True(result.IsSuccess, result.Error);
                var manifest = DatasetManifest.Read(Path.Combine(output, DatasetManifest.FileName), DatasetManifest.Classification);
                var dataset = ClassificationFolderDataset.Load(output);
                Assert.Equal(manifest.Classes, dataset.Classes);
                Assert.Equal(manifest.TrainingCount, dataset.TrainSamples.Count);
                Assert.Equal(manifest.ValidationCount, dataset.ValSamples.Count);
                Assert.Equal(result.Value!.DatasetFingerprint, dataset.Fingerprint);
                var assignments = dataset.TrainSamples.Concat(dataset.ValSamples).ToDictionary(
                    sample => Path.GetRelativePath(output, sample.ImagePath).Replace(Path.DirectorySeparatorChar, '/'));
                Assert.All(manifest.Samples!, sample => Assert.Equal(sample.ClassIndex, assignments[sample.ImagePath].ClassIndex));
                return manifest;
            }

            var byPurpose = TrainingDatasetSplitMode.PurposeAssignment;
            string[] classOrder = ["NG", "OK"];
            var first = await ExportAsync("purpose", byPurpose, classOrder);
            Assert.Equal(2, first.SchemaVersion);
            Assert.Equal("purpose-assignment", first.SplitMethod);
            Assert.Null(first.SplitSeed);
            Assert.Equal(4, first.SplitOverlapCount);
            Assert.Equal(classOrder, first.Classes);

            var automatic = await ExportAsync("automatic", TrainingDatasetSplitMode.AutoSplit, classOrder);
            Assert.Equal(ClassificationSplitPlanner.Method, automatic.SplitMethod);
            Assert.Equal(ClassificationSplitPlanner.Seed, automatic.SplitSeed);
            Assert.Equal(0, automatic.SplitOverlapCount);
            Assert.Equal(2, automatic.TrainingCount);
            Assert.Equal(2, automatic.ValidationCount);

            var reordered = await ExportAsync("reordered", byPurpose, ["OK", "NG"]);
            Assert.NotEqual(first.DatasetFingerprint, reordered.DatasetFingerprint);

            byte[] replacement = [4, 5, 6];
            await File.WriteAllBytesAsync(Path.Combine(images, "a.png"), replacement);
            var changed = await ExportAsync("changed", byPurpose, classOrder);
            Assert.NotEqual(first.DatasetFingerprint, changed.DatasetFingerprint);
            var changedEntries = changed.Samples!.Where(sample => sample.FileName == "a.png").ToArray();
            Assert.Equal(2, changedEntries.Length);
            var replacementHash = Convert.ToHexStringLower(SHA256.HashData(replacement));
            Assert.All(changedEntries, sample => Assert.Equal(replacementHash, sample.ImageSha256));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
