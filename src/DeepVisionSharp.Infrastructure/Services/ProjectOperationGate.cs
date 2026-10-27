using DeepVisionSharp.Domain.ValueObjects;

namespace DeepVisionSharp.Infrastructure.Services;

/// <summary>
/// Excludes deletion from in-flight long operations (training start, gallery/project import)
/// without serializing independent runs.
/// </summary>
internal sealed class ProjectOperationGate
{
    private readonly object _sync = new();
    private readonly Dictionary<Guid, State> _states = new();

    private sealed class State
    {
        internal int Starts;
        internal int GalleryDeletes;
        internal bool Deleting;
        internal bool IsIdle => Starts == 0 && GalleryDeletes == 0 && !Deleting;
    }

    /// <summary>Enters a non-delete long operation (training start, import).</summary>
    internal IDisposable EnterStart(Guid projectId) => EnterStartCore(projectId);

    /// <summary>Enters a project deletion, which excludes every other operation on the project.</summary>
    internal IDisposable EnterDelete(Guid projectId) => EnterDeleteCore(projectId);

    /// <summary>
    /// Enters a gallery deletion. Excludes long operations (import, training) and project deletion,
    /// but not other gallery deletions: those touch different galleries and the write itself is
    /// serialised by SQLite.
    /// </summary>
    internal IDisposable EnterGalleryDelete(Guid projectId) => EnterGalleryDeleteCore(projectId);

    private IDisposable EnterStartCore(Guid id)
    {
        lock (_sync)
        {
            if (!_states.TryGetValue(id, out var state)) _states[id] = state = new();
            if (state.Deleting || state.GalleryDeletes > 0)
                throw new ProjectBusyException("项目正在执行删除操作，请等待操作结束后重试。");
            state.Starts++;
            return CreateLease(id, state, static s => s.Starts--);
        }
    }

    private IDisposable EnterDeleteCore(Guid id)
    {
        lock (_sync)
        {
            if (!_states.TryGetValue(id, out var state)) _states[id] = state = new();
            if (state.Deleting || state.Starts > 0 || state.GalleryDeletes > 0)
                throw new ProjectBusyException("项目正在执行训练或导入，请等待操作结束后重试。");
            state.Deleting = true;
            return CreateLease(id, state, static s => s.Deleting = false);
        }
    }

    private IDisposable EnterGalleryDeleteCore(Guid id)
    {
        lock (_sync)
        {
            if (!_states.TryGetValue(id, out var state)) _states[id] = state = new();
            if (state.Deleting)
                throw new ProjectBusyException("项目正在删除，请等待操作结束后重试。");
            if (state.Starts > 0)
                throw new ProjectBusyException("项目正在执行训练或导入，请等待操作结束后重试。");
            state.GalleryDeletes++;
            return CreateLease(id, state, static s => s.GalleryDeletes--);
        }
    }

    private Lease CreateLease(Guid id, State state, Action<State> release)
    {
        return new Lease(() =>
        {
            lock (_sync)
            {
                release(state);
                if (state.IsIdle) _states.Remove(id);
            }
        });
    }

    private sealed class Lease(Action release) : IDisposable
    {
        private Action? _release = release;
        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}
