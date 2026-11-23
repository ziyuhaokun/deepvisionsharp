using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using DeepVisionSharp.Application.Galleries;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Application.Projects;
using DeepVisionSharp.Infrastructure.Modules;
using DeepVisionSharp.Domain.Entities;
using DeepVisionSharp.Domain.ValueObjects;
using DeepVisionSharp.Infrastructure.Persistence;
using DeepVisionSharp.Infrastructure.Services;
using DeepVisionSharp.Infrastructure.Services.Transfer;

namespace DeepVisionSharp.Infrastructure.Tests;

/// <summary>
/// 项目包的往返：导出再导入后，检测框、模型行、权重文件和训练任务都还在。
/// 直接驱动包的读写，不经过仓储，所以用真实的 SQLite 文件而不是替身。
/// </summary>
public sealed class ProjectPackageRoundTripTests : IDisposable
{
    // Keep test artifacts under the repository's ignored temp directory.
    private readonly string _directory = Path.Combine(
        FindRepositoryTemp(), "pkg-" + Guid.NewGuid().ToString("N"));

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

    public ProjectPackageRoundTripTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        // Pooled SQLite connections can still hold deepvision.db; release them before deleting.
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                System.Data.SQLite.SQLiteConnection.ClearAllPools();
                GC.Collect();
                GC.WaitForPendingFinalizers();
                Directory.Delete(_directory, recursive: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(100);
            }
        }
    }

    [Fact]
    public async Task Package_carries_detection_boxes_models_and_training_jobs()
    {
        var projectId = Guid.NewGuid();
        var galleryId = Guid.NewGuid();
        var packagePath = Path.Combine(_directory, "source.project");

        await CreatePackageAsync(packagePath, projectId, galleryId);

        using var reopened = ProjectPackage.Open(packagePath);

        var annotation = await reopened.Select<SqlAnnotation>().FirstAsync();
        var parsed = LabelmeAnnotationSerializer.Deserialize(annotation.DocumentJson);
        var shapes = LabelmeAnnotationSerializer.ReadShapes(parsed);
        Assert.Single(shapes);
        Assert.Equal("划痕", shapes[0].Label);
        Assert.Equal(10, shapes[0].X);
        Assert.Equal(40, shapes[0].Right);
        Assert.True(LabelmeAnnotationSerializer.ReadReviewed(parsed!.Flags));

        Assert.Single(await reopened.Select<SqlModel>().ToListAsync());
        Assert.Single(await reopened.Select<SqlTrainingJob>().ToListAsync());

        var weight = await reopened.Select<SqlPackageFile>()
            .Where(file => file.RelativePath == "models/best.torchsharp").FirstAsync();
        Assert.Equal("models/best.torchsharp", weight.RelativePath);
        Assert.Equal(new byte[] { 9, 9, 9 }, weight.Content);
    }

    [Fact]
    public async Task Service_round_trip_restores_weights_sidecar_labels_and_training_references()
    {
        var source = Path.Combine(_directory, "source.project");
        var exported = Path.Combine(_directory, "exported;round-trip.project");
        await CreatePackageAsync(source, Guid.NewGuid(), Guid.NewGuid());
        using var services = CreateServices();
        using var db = services.GetRequiredService<SqliteDb>().Instance;
        var transfer = services.GetRequiredService<IProjectSqliteTransferService>();

        var imported = await transfer.ImportAsync(source);
        Assert.True(imported.IsSuccess, imported.Error);
        var projectId = imported.Value!.ProjectId!.Value;
        var galleries = await db.Select<SqlGallery>().Where(row => row.ProjectId == projectId).ToListAsync();
        var gallery = Assert.Single(galleries);
        var model = await db.Select<SqlModel>().Where(row => row.ProjectId == projectId).FirstAsync();
        var job = await db.Select<SqlTrainingJob>().Where(row => row.ProjectId == projectId).FirstAsync();
        var configuration = JsonSerializer.Deserialize<TrainingConfiguration>(job.ConfigurationJson)!;
        Assert.Equal(gallery.Id, configuration.GalleryId);
        Assert.Equal(gallery.Id, Assert.Single(configuration.GalleryIds));
        Assert.True(configuration.GalleryRoles.ContainsKey(gallery.Id));
        Assert.Equal(new byte[] { 9, 9, 9 }, await File.ReadAllBytesAsync(model.CheckpointPath));
        Assert.Equal(new byte[] { 7, 8 }, await File.ReadAllBytesAsync(model.CheckpointPath + ".info"));
        var document = await services.GetRequiredService<IAnnotationWorkflowService>().LoadAsync(gallery.Id, "wafer.jpg");
        Assert.NotNull(document.Shapes);
        Assert.Equal("划痕", Assert.Single(document.Shapes).Label);

        var result = await transfer.ExportAsync(projectId, exported);
        Assert.True(result.IsSuccess, result.Error);
        var secondImport = await transfer.ImportAsync(exported);
        Assert.True(secondImport.IsSuccess, secondImport.Error);
        Assert.NotEqual(projectId, secondImport.Value!.ProjectId);
        Assert.Equal(2, await db.Select<SqlModel>().CountAsync());
        Assert.Equal(2, await db.Select<SqlTrainingJob>().CountAsync());
    }

    [Fact]
    public async Task Cancelled_import_releases_the_gate_and_removes_rows_and_files()
    {
        var source = Path.Combine(_directory, "cancel.project");
        await CreatePackageAsync(source, Guid.NewGuid(), Guid.NewGuid());
        using var services = CreateServices();
        using var db = services.GetRequiredService<SqliteDb>().Instance;
        using var cancellation = new CancellationTokenSource();
        var gate = services.GetRequiredService<ProjectOperationGate>();
        Guid? projectId = null;
        var progress = new InlineProgress(update =>
        {
            if (update.Processed == 1)
            {
                projectId = db.Select<SqlProject>().First().Id;
                Assert.Throws<ProjectBusyException>(() => gate.EnterDelete(projectId.Value));
                cancellation.Cancel();
            }
        });
        var transfer = services.GetRequiredService<IProjectSqliteTransferService>();

        var exception = await Record.ExceptionAsync(async () =>
        {
            var result = await transfer.ImportAsync(source, progress, cancellation.Token);
            Assert.Fail(result.Error ?? "Import completed after cancellation.");
        });
        Assert.True(exception is OperationCanceledException, exception?.ToString());

        Assert.NotNull(projectId);
        Assert.Equal(0, await db.Select<SqlProject>().CountAsync());
        Assert.Equal(0, await db.Select<SqlGallery>().CountAsync());
        var projectDirectory = services.GetRequiredService<IProjectFileStore>().GetProjectDirectory(projectId.Value);
        Assert.False(Directory.Exists(projectDirectory));
        using var lease = gate.EnterDelete(projectId.Value);
    }

    [Fact]
    public async Task Missing_sample_payload_rolls_back_the_import()
    {
        var source = Path.Combine(_directory, "incomplete.project");
        await CreatePackageAsync(source, Guid.NewGuid(), Guid.NewGuid());
        using (var package = ProjectPackage.Open(source, readOnly: false))
        {
            await package.Delete<SqlAnnotation>().Where(row => row.FileName == "wafer.jpg").ExecuteAffrowsAsync();
        }

        using var services = CreateServices();
        using var db = services.GetRequiredService<SqliteDb>().Instance;
        var transfer = services.GetRequiredService<IProjectSqliteTransferService>();
        var result = await transfer.ImportAsync(source);

        Assert.False(result.IsSuccess);
        Assert.Contains("wafer.jpg", result.Error);
        Assert.Equal(0, await db.Select<SqlProject>().CountAsync());
        Assert.Equal(0, await db.Select<SqlGallery>().CountAsync());
        Assert.Empty(Directory.GetDirectories(Path.Combine(_directory, "data", "projects")));
    }

    [Fact]
    public async Task Failed_export_preserves_the_existing_package()
    {
        var source = Path.Combine(_directory, "source.project");
        await CreatePackageAsync(source, Guid.NewGuid(), Guid.NewGuid());
        using var services = CreateServices();
        using var db = services.GetRequiredService<SqliteDb>().Instance;
        var transfer = services.GetRequiredService<IProjectSqliteTransferService>();
        var imported = await transfer.ImportAsync(source);
        Assert.True(imported.IsSuccess, imported.Error);
        var projectId = imported.Value!.ProjectId!.Value;
        var gallery = await db.Select<SqlGallery>().Where(row => row.ProjectId == projectId).FirstAsync();
        var store = services.GetRequiredService<IProjectFileStore>();
        File.Delete(Path.Combine(store.GetGalleryImagesDirectory(projectId, gallery.Id), "wafer.jpg"));
        var before = await File.ReadAllBytesAsync(source);

        var result = await transfer.ExportAsync(projectId, source);

        Assert.False(result.IsSuccess);
        Assert.Equal(before, await File.ReadAllBytesAsync(source));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public async Task Legacy_package_without_optional_tables_imports_successfully()
    {
        var source = Path.Combine(_directory, "legacy.project");
        await CreatePackageAsync(source, Guid.NewGuid(), Guid.NewGuid());
        using (var package = ProjectPackage.Open(source, readOnly: false))
        {
            await package.Ado.ExecuteNonQueryAsync("DROP TABLE PackageFiles; DROP TABLE Models; DROP TABLE TrainingJobs;");
        }

        using var services = CreateServices();
        using var db = services.GetRequiredService<SqliteDb>().Instance;
        var imported = await services.GetRequiredService<IProjectSqliteTransferService>().ImportAsync(source);

        Assert.True(imported.IsSuccess, imported.Error);
        Assert.Equal(1, await db.Select<SqlSample>().CountAsync());
    }

    private ServiceProvider CreateServices() => new ServiceCollection()
        .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
        .AddSingleton<IDataRootProvider>(new DefaultDataRootProvider(Path.Combine(_directory, "data")))
        .AddDataModule()
        .BuildServiceProvider();

    private sealed class InlineProgress(Action<ProjectTransferProgress> report) : IProgress<ProjectTransferProgress>
    {
        public void Report(ProjectTransferProgress value) => report(value);
    }

    private static async Task CreatePackageAsync(string packagePath, Guid projectId, Guid galleryId)
    {
        using (var package = ProjectPackage.Create(packagePath))
        {
            await package.Insert(new SqlProject
            {
                Id = projectId,
                Name = "检测项目",
                Description = "往返测试",
                Type = "Detection",
                CreatedUtc = DateTime.UtcNow,
                LastUpdatedUtc = DateTime.UtcNow
            }).ExecuteAffrowsAsync();

            await package.Insert(new SqlGallery
            {
                Id = galleryId,
                ProjectId = projectId,
                Name = "图库",
                SampleCount = 1,
                LastUpdatedUtc = DateTime.UtcNow
            }).ExecuteAffrowsAsync();

            await package.Insert(new SqlSample
            {
                Id = Guid.NewGuid(),
                GalleryId = galleryId,
                FileName = "wafer.jpg",
                IsAnnotated = true
            }).ExecuteAffrowsAsync();

            var document = new LabelmeDocument
            {
                Version = "5.0.1",
                ImagePath = "wafer.jpg",
                ImageWidth = 640,
                ImageHeight = 480,
                Flags = LabelmeAnnotationSerializer.BuildDocumentFlags(null, reviewed: true),
                Shapes = LabelmeAnnotationSerializer.ToLabelmeShapes(
                [
                    new AnnotationShape("划痕", AnnotationShapeKind.Rectangle, 10, 20, 30, 40)
                ])
            };

            await package.Insert(new SqlAnnotation
            {
                GalleryId = galleryId,
                FileName = "wafer.jpg",
                Width = 640,
                Height = 480,
                ImageData = Convert.ToBase64String([1, 2, 3, 4]),
                DocumentJson = LabelmeAnnotationSerializer.Serialize(document)
            }).ExecuteAffrowsAsync();

            await package.Insert(new SqlModel
            {
                Id = Guid.NewGuid(),
                ProjectId = projectId,
                Name = "检测模型",
                ModelType = "yolo26n-det",
                Status = "Ready",
                ClassesJson = "[\"划痕\"]",
                CheckpointPath = @"C:\data\projects\" + projectId + @"\models\best.torchsharp",
                CreatedUtc = DateTime.UtcNow
            }).ExecuteAffrowsAsync();

            await package.Insert(new SqlPackageFile
            {
                RelativePath = "models/best.torchsharp",
                Content = [9, 9, 9]
            }).ExecuteAffrowsAsync();

            await package.Insert(new SqlPackageFile
            {
                RelativePath = "models/best.torchsharp.info",
                Content = [7, 8]
            }).ExecuteAffrowsAsync();

            await package.Insert(new SqlTrainingJob
            {
                Id = Guid.NewGuid(),
                ProjectId = projectId,
                Name = "第一次训练",
                Backend = "torchsharp:detection-train",
                ModelType = "yolo26n-det",
                Status = "Completed",
                ConfigurationJson = JsonSerializer.Serialize(new TrainingConfiguration(
                    50, 8, 0.005, "sgd", galleryId: galleryId, galleryIds: [galleryId],
                    galleryRoles: new Dictionary<Guid, GalleryDatasetRole> { [galleryId] = GalleryDatasetRole.Training }))
            }).ExecuteAffrowsAsync();
        }
    }
}
