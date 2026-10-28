using Microsoft.Extensions.Logging.Abstractions;

using DeepVisionSharp.Application.Training;
using DeepVisionSharp.Domain.Common;
using DeepVisionSharp.Domain.Entities;
using DeepVisionSharp.Domain.Repositories;
using DeepVisionSharp.Infrastructure.Services;
using Xunit;

namespace DeepVisionSharp.Infrastructure.Tests;

public sealed class TrainingRunWorkerHostedServiceTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task StopAsync_before_worker_consumes_marks_pending_job_cancelled_and_releases_lease()
    {
        var fixture = new Fixture();
        var job = fixture.AddJob(TrainingJobStatus.Preparing);
        var lease = new TrackingLease();
        Assert.True(fixture.Queue.TryEnqueue(fixture.Request(job, new FakeBackend(), lease), out var error), error);

        // The worker was never started, so the item can only be drained by Shutdown.
        await fixture.Worker.StopAsync(CancellationToken.None).WaitAsync(Timeout);

        Assert.Equal(TrainingJobStatus.Cancelled, fixture.Repository.Get(job.Id).Status);
        Assert.Equal(1, lease.DisposeCount);
        Assert.False(fixture.Queue.IsTracked(job.Id));
        Assert.False(fixture.Queue.TryEnqueue(fixture.Request(job, new FakeBackend(), new TrackingLease()), out _));
    }

    [Fact]
    public async Task StopAsync_cancels_running_backend_and_lands_job_cancelled()
    {
        var fixture = new Fixture();
        var job = fixture.AddJob(TrainingJobStatus.Running);
        var lease = new TrackingLease();
        var backend = new FakeBackend { Behavior = FakeBackend.Mode.WaitForCancellation };
        Assert.True(fixture.Queue.TryEnqueue(fixture.Request(job, backend, lease), out var error), error);

        await fixture.Worker.StartAsync(CancellationToken.None);
        await backend.Started.WaitAsync(Timeout);

        await fixture.Worker.StopAsync(CancellationToken.None).WaitAsync(Timeout);

        Assert.True(backend.ObservedCancellation);
        Assert.Equal(TrainingJobStatus.Cancelled, fixture.Repository.Get(job.Id).Status);
        Assert.Equal(1, lease.DisposeCount);
        Assert.False(fixture.Queue.HasAny);
    }

    [Fact]
    public async Task StopAsync_when_backend_returns_failure_after_cancel_marks_cancelled_not_failed()
    {
        var fixture = new Fixture();
        var job = fixture.AddJob(TrainingJobStatus.Running);
        var lease = new TrackingLease();
        var backend = new FakeBackend { Behavior = FakeBackend.Mode.FailAfterCancellation };
        Assert.True(fixture.Queue.TryEnqueue(fixture.Request(job, backend, lease), out var error), error);

        await fixture.Worker.StartAsync(CancellationToken.None);
        await backend.Started.WaitAsync(Timeout);
        await fixture.Worker.StopAsync(CancellationToken.None).WaitAsync(Timeout);

        Assert.Equal(TrainingJobStatus.Cancelled, fixture.Repository.Get(job.Id).Status);
        Assert.Equal(1, lease.DisposeCount);
        Assert.False(fixture.Queue.HasAny);
    }

    [Fact]
    public async Task StopAsync_twice_is_safe_and_does_not_double_release()
    {
        var fixture = new Fixture();
        var job = fixture.AddJob(TrainingJobStatus.Running);
        var lease = new TrackingLease();
        var backend = new FakeBackend { Behavior = FakeBackend.Mode.WaitForCancellation };
        Assert.True(fixture.Queue.TryEnqueue(fixture.Request(job, backend, lease), out var error), error);

        await fixture.Worker.StartAsync(CancellationToken.None);
        await backend.Started.WaitAsync(Timeout);

        await fixture.Worker.StopAsync(CancellationToken.None).WaitAsync(Timeout);
        await fixture.Worker.StopAsync(CancellationToken.None).WaitAsync(Timeout);

        Assert.Equal(TrainingJobStatus.Cancelled, fixture.Repository.Get(job.Id).Status);
        Assert.Equal(1, lease.DisposeCount);
    }

    [Fact]
    public async Task StopAsync_with_idle_worker_completes_without_touching_jobs()
    {
        var fixture = new Fixture();

        await fixture.Worker.StartAsync(CancellationToken.None);
        await fixture.Worker.StopAsync(CancellationToken.None).WaitAsync(Timeout);

        Assert.False(fixture.Queue.HasAny);
        Assert.Equal(0, fixture.Repository.SaveCount);
    }

    [Fact]
    public async Task Backend_exception_without_stop_marks_job_failed_and_releases_lease()
    {
        var fixture = new Fixture();
        var job = fixture.AddJob(TrainingJobStatus.Running);
        var lease = new TrackingLease();
        var backend = new FakeBackend { Behavior = FakeBackend.Mode.Throw };
        Assert.True(fixture.Queue.TryEnqueue(fixture.Request(job, backend, lease), out var error), error);

        await fixture.Worker.StartAsync(CancellationToken.None);
        await fixture.Repository.WaitForStatusAsync(job.Id, TrainingJobStatus.Failed, Timeout);
        await fixture.Worker.StopAsync(CancellationToken.None).WaitAsync(Timeout);

        Assert.Equal("boom", fixture.Repository.Get(job.Id).FailureMessage);
        Assert.Equal(1, lease.DisposeCount);
        Assert.False(fixture.Queue.HasAny);
    }

    private sealed class Fixture
    {
        public Fixture()
        {
            Queue = new TrainingRunQueue();
            Repository = new FakeRepository();
            var writer = new TrainingJobStatusWriter(Repository, NullLogger<TrainingJobStatusWriter>.Instance);
            Worker = new TrainingRunWorkerHostedService(
                Queue,
                writer,
                Repository,
                NullLogger<TrainingRunWorkerHostedService>.Instance);
        }

        public TrainingRunQueue Queue { get; }

        public FakeRepository Repository { get; }

        public TrainingRunWorkerHostedService Worker { get; }

        public TrainingJob AddJob(TrainingJobStatus status)
        {
            var job = TrainingJob.FromPersistence(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "job",
                "torchsharp:classification-train",
                "resnet18",
                dataRoot: null,
                outputRoot: null,
                status,
                startedAt: null,
                finishedAt: null,
                bestModelPath: null,
                lastModelPath: null,
                logFile: null,
                processId: null,
                configuration: null,
                failureMessage: null);
            Repository.Seed(job);
            return job;
        }

        public TrainingRunRequest Request(TrainingJob job, ITrainingBackend backend, IDisposable lease)
            => new(job.Id, job.ProjectId ?? Guid.Empty, Context: null!, backend, lease);
    }

    private sealed class FakeRepository : ITrainingJobRepository
    {
        private readonly object _sync = new();
        private readonly Dictionary<Guid, TrainingJob> _jobs = new();
        private int _saveCount;

        public int SaveCount => Volatile.Read(ref _saveCount);

        public void Seed(TrainingJob job)
        {
            lock (_sync)
            {
                _jobs[job.Id] = job;
            }
        }

        public TrainingJob Get(Guid id)
        {
            lock (_sync)
            {
                return _jobs[id];
            }
        }

        public async Task WaitForStatusAsync(Guid id, TrainingJobStatus status, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (Get(id).Status == status)
                {
                    return;
                }

                await Task.Delay(10);
            }

            throw new TimeoutException($"Job {id} never reached {status}; last status {Get(id).Status}.");
        }

        public Task<TrainingJob?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            lock (_sync)
            {
                return Task.FromResult(_jobs.GetValueOrDefault(id));
            }
        }

        public Task SaveAsync(TrainingJob job, CancellationToken cancellationToken = default)
        {
            lock (_sync)
            {
                _jobs[job.Id] = job;
            }

            Interlocked.Increment(ref _saveCount);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<TrainingJob>> GetAllAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<TrainingJob>> GetByProjectIdAsync(Guid projectId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<TrainingJob>> GetRunningAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<TrainingJob>> GetByStatusesAsync(
            IReadOnlyList<TrainingJobStatus> statuses,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeBackend : ITrainingBackend
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _observedCancellation;

        public enum Mode
        {
            /// <summary>Throws <see cref="InvalidOperationException"/> immediately.</summary>
            Throw,

            /// <summary>Blocks until cancelled, then lets the <see cref="OperationCanceledException"/> escape.</summary>
            WaitForCancellation,

            /// <summary>Blocks until cancelled, then reports a failed result instead of throwing.</summary>
            FailAfterCancellation
        }

        public Mode Behavior { get; init; } = Mode.Throw;

        public Task Started => _started.Task;

        public bool ObservedCancellation => Volatile.Read(ref _observedCancellation) == 1;

        public string BackendKey => "fake";

        public bool CanHandle(string? modelType) => true;

        public Task<Result> ValidateAsync(TrainingStartValidationContext context, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public async Task<Result<TrainingBackendStartResult>> StartAsync(
            TrainingStartContext context,
            CancellationToken cancellationToken)
        {
            _started.TrySetResult();
            switch (Behavior)
            {
                case Mode.WaitForCancellation:
                    try
                    {
                        await Task.Delay(System.Threading.Timeout.Infinite, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        Interlocked.Exchange(ref _observedCancellation, 1);
                        throw;
                    }

                    break;
                case Mode.FailAfterCancellation:
                    try
                    {
                        await Task.Delay(System.Threading.Timeout.Infinite, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        Interlocked.Exchange(ref _observedCancellation, 1);
                    }

                    return Result<TrainingBackendStartResult>.Failure("cancelled by host");
                default:
                    throw new InvalidOperationException("boom");
            }

            return Result<TrainingBackendStartResult>.Failure("unreachable");
        }

        public Task<Result> StopAsync(TrainingJob job, string? reason, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<TrainingRecoveryDecision> EvaluateRecoveryAsync(TrainingJob job, CancellationToken cancellationToken)
            => throw new NotSupportedException();

    }

    private sealed class TrackingLease : IDisposable
    {
        private int _disposeCount;

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public void Dispose() => Interlocked.Increment(ref _disposeCount);
    }
}
