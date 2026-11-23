using DeepVisionSharp.Application;
using DeepVisionSharp.Application.Models;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Domain.Entities;
using DeepVisionSharp.Domain.Repositories;
using DeepVisionSharp.Domain.ValueObjects;
using DeepVisionSharp.Infrastructure.Persistence;
using DeepVisionSharp.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DeepVisionSharp.Infrastructure.Tests;

/// <summary>Stale snapshots must not overwrite a newer training status in SQLite.</summary>
[Collection(SqliteSerialCollection.Name)]
public sealed class TrainingStatusTransitionTests : IDisposable
{
    private readonly DirectoryInfo _root;

    public TrainingStatusTransitionTests()
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(repository.FullName, "DeepVisionSharp.slnx")))
        {
            repository = repository.Parent ?? throw new DirectoryNotFoundException("DeepVisionSharp.slnx");
        }

        _root = Directory.CreateDirectory(Path.Combine(repository.FullName, "temp", $"transition-{Guid.NewGuid():N}"));
    }

    [Fact]
    public async Task First_terminal_state_wins_over_stale_snapshots()
    {
        using var services = CreateServices();
        using var db = services.GetRequiredService<SqliteDb>().Instance;
        var (writer, repository, running) = await SeedAsync(services, TrainingJobStatus.Running);
        var events = new List<TrainingJobStatus>();
        writer.JobStatusChanged += (_, status, _) => events.Add(status);

        await writer.MarkCancelledAsync(running);
        // Every writer below still holds the Running snapshot.
        await writer.MarkFailedAsync(running, failureMessage: "late failure");
        var completed = await writer.CompleteAsync(running, TrainingJobStatus.Succeeded, "best.torchsharp");
        var restarted = await writer.MarkRunningAsync(running, 0, CancellationToken.None);

        Assert.True(completed.IsFailure);
        Assert.True(restarted.IsFailure);
        Assert.Equal(TrainingJobStatus.Cancelled, (await repository.GetByIdAsync(running.Id))!.Status);
        Assert.Equal(new[] { TrainingJobStatus.Cancelled }, events);
    }

    [Fact]
    public async Task Succeeded_job_is_not_overwritten_by_a_late_failure_or_cancel()
    {
        using var services = CreateServices();
        using var db = services.GetRequiredService<SqliteDb>().Instance;
        var (writer, repository, running) = await SeedAsync(services, TrainingJobStatus.Running);

        var completed = await writer.CompleteAsync(running, TrainingJobStatus.Succeeded, "best.torchsharp", "last.torchsharp");
        Assert.True(completed.IsSuccess, completed.Error);
        await writer.MarkFailedAsync(running, failureMessage: "late failure");
        await writer.MarkCancelledAsync(running);

        var stored = (await repository.GetByIdAsync(running.Id))!;
        Assert.Equal(TrainingJobStatus.Succeeded, stored.Status);
        Assert.Equal("best.torchsharp", stored.BestModelPath);
        Assert.Null(stored.FailureMessage);
    }

    [Fact]
    public async Task Racing_completion_and_cancel_leave_exactly_one_terminal_state()
    {
        for (var round = 0; round < 10; round++)
        {
            using var services = CreateServices(round);
            using var db = services.GetRequiredService<SqliteDb>().Instance;
            var (writer, repository, running) = await SeedAsync(services, TrainingJobStatus.Running);
            var events = new List<TrainingJobStatus>();
            writer.JobStatusChanged += (_, status, _) => { lock (events) events.Add(status); };

            await Task.WhenAll(
                Task.Run(() => writer.CompleteAsync(running, TrainingJobStatus.Succeeded, "best.torchsharp")),
                Task.Run(() => writer.MarkCancelledAsync(running)));

            var stored = (await repository.GetByIdAsync(running.Id))!;
            Assert.Contains(stored.Status, new[] { TrainingJobStatus.Succeeded, TrainingJobStatus.Cancelled });
            Assert.Equal(new[] { stored.Status }, events);
        }
    }

    [Fact]
    public async Task Legitimate_transitions_still_persist()
    {
        using var services = CreateServices();
        using var db = services.GetRequiredService<SqliteDb>().Instance;
        var (writer, repository, preparing) = await SeedAsync(services, TrainingJobStatus.Preparing);

        var running = await writer.MarkRunningAsync(preparing, 0, CancellationToken.None, dataRoot: "data");
        Assert.True(running.IsSuccess, running.Error);
        var again = await writer.MarkRunningAsync(running.Value!, 0, CancellationToken.None, dataRoot: "data2");
        Assert.True(again.IsSuccess, again.Error);
        await writer.MarkFailedAsync(again.Value!, failureMessage: "boom");
        await writer.MarkFailedAsync(again.Value!, failureMessage: "boom again");

        var stored = (await repository.GetByIdAsync(preparing.Id))!;
        Assert.Equal(TrainingJobStatus.Failed, stored.Status);
        Assert.Equal("boom again", stored.FailureMessage);
        Assert.Equal("data2", stored.DataRoot);
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

    private ServiceProvider CreateServices(int round = 0) => new ServiceCollection()
        .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
        .AddSingleton<IDataRootProvider>(new DefaultDataRootProvider(Path.Combine(_root.FullName, $"data{round}")))
        .AddDeepVisionApplication()
        .AddDeepVisionInfrastructure()
        .BuildServiceProvider();

    private static async Task<(TrainingJobStatusWriter Writer, ITrainingJobRepository Repository, TrainingJob Job)> SeedAsync(
        ServiceProvider services, TrainingJobStatus status)
    {
        var db = services.GetRequiredService<SqliteDb>().Instance;
        var projectId = Guid.NewGuid();
        await db.Insert(new SqlProject
        {
            Id = projectId,
            Name = "Transitions",
            Type = ProjectTypes.Classification,
            CreatedUtc = DateTime.UtcNow,
            LastUpdatedUtc = DateTime.UtcNow
        }).ExecuteAffrowsAsync();
        var job = new TrainingJob(Guid.NewGuid(), projectId, "Train", BackendKeys.TorchSharpClassification,
            ModelTypeNames.ResNet18, dataRoot: null, outputRoot: null, status, DateTime.UtcNow, finishedAt: null,
            bestModelPath: null, lastModelPath: null, logFile: null, processId: 0, configuration: null, failureMessage: null);
        var repository = services.GetRequiredService<ITrainingJobRepository>();
        await repository.SaveAsync(job);
        return (services.GetRequiredService<TrainingJobStatusWriter>(), repository, job);
    }
}
