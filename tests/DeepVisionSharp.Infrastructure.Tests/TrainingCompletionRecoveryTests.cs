using System.Text.Json;
using System.Text.Json.Nodes;
using DeepVisionSharp.Application;
using DeepVisionSharp.Application.Models;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Application.Training;
using DeepVisionSharp.Domain.Common;
using DeepVisionSharp.Domain.Entities;
using DeepVisionSharp.Domain.Repositories;
using DeepVisionSharp.Domain.ValueObjects;
using DeepVisionSharp.Infrastructure.Services;
using DeepVisionSharp.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DeepVisionSharp.Infrastructure.Tests;

[Collection(SqliteSerialCollection.Name)]
public sealed class TrainingCompletionRecoveryTests : IDisposable
{
    private readonly DirectoryInfo _root;

    public TrainingCompletionRecoveryTests()
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(repository.FullName, "DeepVisionSharp.slnx")))
        {
            repository = repository.Parent ?? throw new DirectoryNotFoundException("DeepVisionSharp.slnx");
        }

        _root = Directory.CreateDirectory(Path.Combine(repository.FullName, "temp", $"completion-{Guid.NewGuid():N}"));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task Reopens_database_and_recovers_completed_job_after_status_write_fails(bool detection, bool cancelAfterPublication)
    {
        TrainingJob job;
        TrainingCompletionRecord completion;
        using (var services = CreateServices())
        using (var db = services.GetRequiredService<SqliteDb>().Instance)
        {
            job = await SeedJobAsync(services, detection);
            var (best, last) = WriteArtifacts(job);
            // Reject both the expected Succeeded write and any retry while allowing a buggy
            // Failed/Cancelled write to succeed. The job must stay active for reconciliation.
            db.Ado.ExecuteNonQuery("""
                CREATE TRIGGER reject_training_success BEFORE INSERT ON TrainingJobs
                WHEN NEW.Status = 'Succeeded' BEGIN SELECT RAISE(ABORT, 'injected status write failure'); END;
                CREATE TRIGGER reject_training_success_update BEFORE UPDATE ON TrainingJobs
                WHEN NEW.Status = 'Succeeded' BEGIN SELECT RAISE(ABORT, 'injected status write failure'); END;
                """);

            var writer = services.GetRequiredService<TrainingJobStatusWriter>();
            var backend = new CompletionBackend(async token =>
            {
                var queue = services.GetRequiredService<TrainingRunQueue>();
                var published = queue.PublishCompletion(job, best, best + ".info", last, token);
                var finished = await writer.CompletePublishedAsync(job, best, last, published.CompletedUtc, CancellationToken.None);
                Assert.True(finished.IsFailure);
                if (cancelAfterPublication)
                {
                    queue.TryCancel(job.Id);
                    token.ThrowIfCancellationRequested();
                }

                return Result<TrainingBackendStartResult>.Failure(finished.Error!);
            });
            await RunWorkerAsync(services, job, backend);
            await writer.MarkFailedAsync(job, failureMessage: "late fallback error");
            await writer.MarkCancelledAsync(job);
            var stop = await services.GetRequiredService<ITrainingJobLifecycleService>().StopAsync(job.Id);
            Assert.True(stop.IsFailure);
            Assert.Contains("训练产物已保存", stop.Error);
            var repository = services.GetRequiredService<ITrainingJobRepository>();
            Assert.Equal(TrainingJobStatus.Running, (await repository.GetByIdAsync(job.Id))!.Status);
            completion = Assert.IsType<TrainingCompletionRecord>(TrainingCompletionRecord.Read(job));
            var recoveryWhileBlocked = await services.GetRequiredService<ITrainingJobLifecycleService>().ReconcileOrphanedJobsAsync();
            Assert.Equal(0, recoveryWhileBlocked.Value);
            Assert.Equal(TrainingJobStatus.Running, (await repository.GetByIdAsync(job.Id))!.Status);
            db.Ado.ExecuteNonQuery("DROP TRIGGER reject_training_success; DROP TRIGGER reject_training_success_update;");
        }

        using var restarted = CreateServices();
        using var restartedDb = restarted.GetRequiredService<SqliteDb>().Instance;
        var notifications = new List<TrainingJobStatus>();
        restarted.GetRequiredService<TrainingJobStatusWriter>().JobStatusChanged += (_, status, _) => notifications.Add(status);
        var lifecycle = restarted.GetRequiredService<ITrainingJobLifecycleService>();
        var counts = await Task.WhenAll(lifecycle.ReconcileOrphanedJobsAsync(), lifecycle.ReconcileOrphanedJobsAsync());
        Assert.All(counts, count => Assert.True(count.IsSuccess, count.Error));
        Assert.Equal(1, counts.Sum(count => count.Value));
        Assert.Equal(TrainingJobStatus.Succeeded, Assert.Single(notifications));
        var recovered = await restarted.GetRequiredService<ITrainingJobRepository>().GetByIdAsync(job.Id);
        Assert.NotNull(recovered);
        Assert.Equal(TrainingJobStatus.Succeeded, recovered.Status);
        Assert.Equal(completion.BestPath(job.OutputRoot!), recovered.BestModelPath);
        Assert.Equal(completion.LastPath(job.OutputRoot!), recovered.LastModelPath);
        // The SQLite provider persists these timestamps with whole-second precision.
        Assert.Equal(completion.CompletedUtc.Ticks / TimeSpan.TicksPerSecond,
            recovered.FinishedAt!.Value.Ticks / TimeSpan.TicksPerSecond);
        Assert.False(File.Exists(job.LogFile));
    }

    [Fact]
    public async Task Cancellation_before_publication_keeps_checkpoints_and_skips_completion()
    {
        using var services = CreateServices();
        using var db = services.GetRequiredService<SqliteDb>().Instance;
        var job = await SeedJobAsync(services, detection: false);
        var (best, last) = WriteArtifacts(job);
        var backend = new CompletionBackend(token =>
        {
            var queue = services.GetRequiredService<TrainingRunQueue>();
            queue.TryCancel(job.Id);
            queue.PublishCompletion(job, best, best + ".info", last, token);
            throw new InvalidOperationException("Expected cancellation before publication.");
        });
        await RunWorkerAsync(services, job, backend);
        var persisted = await services.GetRequiredService<ITrainingJobRepository>().GetByIdAsync(job.Id);
        Assert.NotNull(persisted);
        Assert.Equal(TrainingJobStatus.Cancelled, persisted.Status);
        Assert.Equal(best, persisted.BestModelPath);
        Assert.Equal(last, persisted.LastModelPath);
        Assert.Null(TrainingCompletionRecord.Read(job));
        var reconcile = await services.GetRequiredService<ITrainingJobLifecycleService>().ReconcileOrphanedJobsAsync();
        Assert.Equal(0, reconcile.Value);
    }

    [Theory]
    [InlineData("wrong job")]
    [InlineData("traversal")]
    [InlineData("missing last info")]
    [InlineData("invalid record")]
    [InlineData("wrong sidecar job")]
    public async Task Invalid_record_reports_the_problem_even_when_legacy_log_exists(string damage)
    {
        using var services = CreateServices();
        using var db = services.GetRequiredService<SqliteDb>().Instance;
        var job = await SeedJobAsync(services, detection: true);
        var (best, last) = WriteArtifacts(job);
        TrainingCompletionRecord.Publish(job, best, best + ".info", last, CancellationToken.None);
        WriteLegacyLog(job, best, "detect");
        var path = Path.Combine(job.OutputRoot!, TrainingCompletionRecord.FileName);
        switch (damage)
        {
            case "wrong job":
            case "traversal":
                var record = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
                if (damage == "wrong job") record["job_id"] = Guid.NewGuid();
                else record["best_checkpoint"] = "../best.torchsharp";
                await File.WriteAllTextAsync(path, record.ToJsonString());
                break;
            case "missing last info":
                File.Delete(last + ".info");
                break;
            case "wrong sidecar job":
                var sidecar = JsonNode.Parse(await File.ReadAllTextAsync(last + ".info"))!.AsObject();
                sidecar["training_job_id"] = Guid.NewGuid();
                await File.WriteAllTextAsync(last + ".info", sidecar.ToJsonString());
                break;
            default:
                await File.WriteAllTextAsync(path, "{invalid");
                break;
        }

        var result = await services.GetRequiredService<ITrainingJobLifecycleService>().ReconcileOrphanedJobsAsync();
        Assert.Equal(1, result.Value);
        var persisted = await services.GetRequiredService<ITrainingJobRepository>().GetByIdAsync(job.Id);
        Assert.NotNull(persisted);
        Assert.Equal(TrainingJobStatus.Failed, persisted.Status);
        Assert.Contains("训练完成记录无效", persisted.FailureMessage);
    }

    [Fact]
    public async Task Legacy_log_recovers_the_checkpoint_it_names_even_if_another_best_exists()
    {
        using var services = CreateServices();
        using var db = services.GetRequiredService<SqliteDb>().Instance;
        var job = await SeedJobAsync(services, detection: false);
        var (best, last) = WriteArtifacts(job);
        var otherDirectory = Directory.CreateDirectory(Path.Combine(job.OutputRoot!, "train", "weights"));
        File.Copy(best, Path.Combine(otherDirectory.FullName, "best.pt"));
        File.Copy(best + ".info", Path.Combine(otherDirectory.FullName, "best.pt.info"));
        WriteLegacyLog(job, best);

        var result = await services.GetRequiredService<ITrainingJobLifecycleService>().ReconcileOrphanedJobsAsync();
        Assert.Equal(1, result.Value);
        var persisted = await services.GetRequiredService<ITrainingJobRepository>().GetByIdAsync(job.Id);
        Assert.NotNull(persisted);
        Assert.Equal(TrainingJobStatus.Succeeded, persisted.Status);
        Assert.Equal(best, persisted.BestModelPath);
        Assert.Equal(last, persisted.LastModelPath);
    }

    public void Dispose()
    {
        // Temp data under the repository's ignored temp/ folder: cleanup must never fail a test.
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                System.Data.SQLite.SQLiteConnection.ClearAllPools();
                GC.Collect();
                GC.WaitForPendingFinalizers();
                _root.Delete(recursive: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(100);
            }
        }
    }

    private ServiceProvider CreateServices() => new ServiceCollection()
        .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
        .AddSingleton<IDataRootProvider>(new DefaultDataRootProvider(Path.Combine(_root.FullName, "data")))
        .AddDeepVisionApplication()
        .AddDeepVisionInfrastructure()
        .BuildServiceProvider();

    private async Task<TrainingJob> SeedJobAsync(ServiceProvider services, bool detection)
    {
        var db = services.GetRequiredService<SqliteDb>().Instance;
        var projectId = Guid.NewGuid();
        await db.Insert(new SqlProject
        {
            Id = projectId,
            Name = "Recovery",
            Type = detection ? ProjectTypes.Detection : ProjectTypes.Classification,
            CreatedUtc = DateTime.UtcNow,
            LastUpdatedUtc = DateTime.UtcNow
        }).ExecuteAffrowsAsync();
        var jobId = Guid.NewGuid();
        var plan = services.GetRequiredService<ITrainingWorkspacePlanner>().CreateRunPlan(projectId, jobId, "Train");
        var job = new TrainingJob(jobId, projectId, "Train",
            detection ? BackendKeys.TorchSharpDetection : BackendKeys.TorchSharpClassification,
            detection ? ModelTypeNames.Yolo26NDet : ModelTypeNames.ResNet18,
            dataRoot: null, plan.OutputRoot, TrainingJobStatus.Running, DateTime.UtcNow, finishedAt: null,
            bestModelPath: null, lastModelPath: null, plan.LogFile, processId: 0, configuration: null, failureMessage: null);
        await services.GetRequiredService<ITrainingJobRepository>().SaveAsync(job);
        return job;
    }

    private static (string Best, string Last) WriteArtifacts(TrainingJob job)
    {
        var best = Path.Combine(job.OutputRoot!, "best.torchsharp");
        var last = Path.Combine(job.OutputRoot!, "last.torchsharp");
        var detection = ModelTypeNames.IsDetectionModelType(job.ModelType);
        var metadata = JsonSerializer.Serialize(new
        {
            task = detection ? "detect" : "classify",
            artifact_kind = "trained_model",
            model_type = job.ModelType,
            training_job_id = job.Id,
            dataset_fingerprint = "recovery-test",
            classes = new[] { "NG", "OK" },
            weights_format = "torchsharp",
            arch = detection ? "yolo26n-det" : "resnet18_v2",
            preprocessing = detection ? "letterbox_v1" : "unit_scale_v1",
            input_width = 64,
            input_height = 64
        });
        foreach (var path in new[] { best, last })
        {
            File.WriteAllBytes(path, [1, 2, 3]);
            File.WriteAllText(path + ".info", metadata);
        }

        return (best, last);
    }

    private static void WriteLegacyLog(TrainingJob job, string best, string? task = null) => File.WriteAllText(job.LogFile!,
        TrainingCompletionSignals.CompletedPrefix + JsonSerializer.Serialize(new
        {
            best_model_path = Path.GetFileName(best),
            metadata_path = Path.GetFileName(best) + ".info",
            task
        }));

    private static async Task RunWorkerAsync(ServiceProvider services, TrainingJob job, ITrainingBackend backend)
    {
        var queue = services.GetRequiredService<TrainingRunQueue>();
        var lease = new TrackingLease();
        var context = new TrainingStartContext(null!, job, null!, [], new Dictionary<Guid, int>(), [], []);
        Assert.True(queue.TryEnqueue(new TrainingRunRequest(job.Id, job.ProjectId!.Value, context, backend, lease), out var error), error);
        using var worker = new TrainingRunWorkerHostedService(queue, services.GetRequiredService<TrainingJobStatusWriter>(),
            services.GetRequiredService<ITrainingJobRepository>(), NullLogger<TrainingRunWorkerHostedService>.Instance);
        await worker.StartAsync(CancellationToken.None);
        try
        {
            await lease.Released.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    private sealed class TrackingLease : IDisposable
    {
        public TaskCompletionSource Released { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Dispose() => Released.TrySetResult();
    }

    private sealed class CompletionBackend(Func<CancellationToken, Task<Result<TrainingBackendStartResult>>> finish) : ITrainingBackend
    {
        public string BackendKey => "test-completion";
        public bool CanHandle(string? modelType) => true;
        public Task<Result<TrainingBackendStartResult>> StartAsync(TrainingStartContext context, CancellationToken cancellationToken) => finish(cancellationToken);
        public Task<Result> ValidateAsync(TrainingStartValidationContext context, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result> StopAsync(TrainingJob job, string? reason, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<TrainingRecoveryDecision> EvaluateRecoveryAsync(TrainingJob job, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
