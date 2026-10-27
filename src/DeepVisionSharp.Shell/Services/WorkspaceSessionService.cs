using DeepVisionSharp.Domain.Entities;
using DeepVisionSharp.Domain.ValueObjects;

namespace DeepVisionSharp.Shell.Services;

public sealed record WorkspaceSessionSnapshot(
    Project? Project,
    Guid? GalleryId,
    long Version)
{
    public static WorkspaceSessionSnapshot Empty { get; } = new(null, null, 0);
}

public enum WorkspaceSessionChangeReason
{
    ProjectEntered,
    ProjectUpdated,
    ProjectCleared,
    GalleryEntered,
    GalleryCleared,
}

public sealed class WorkspaceSessionChangedEventArgs(
    WorkspaceSessionSnapshot previous,
    WorkspaceSessionSnapshot current,
    WorkspaceSessionChangeReason reason) : EventArgs
{
    public WorkspaceSessionSnapshot Previous { get; } = previous;

    public WorkspaceSessionSnapshot Current { get; } = current;

    public WorkspaceSessionChangeReason Reason { get; } = reason;
}

/// <summary>
/// Process-wide workspace session (project/gallery selection).
/// Snapshot mutations are synchronized so background loads can safely call
/// <see cref="IsCurrent"/> while the UI thread updates the session.
/// Event handlers run outside the lock.
/// </summary>
public sealed class WorkspaceSessionService
{
    private readonly object _gate = new();
    private WorkspaceSessionSnapshot _current = WorkspaceSessionSnapshot.Empty;

    public event EventHandler<WorkspaceSessionChangedEventArgs>? Changed;

    public WorkspaceSessionSnapshot Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public void EnterProject(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        ProjectCapabilityCatalog.EnsureActive(project.Type);

        Publish(
            previous => new WorkspaceSessionSnapshot(project, null, NextVersion(previous)),
            WorkspaceSessionChangeReason.ProjectEntered);
    }

    public bool TryUpdateCurrentProject(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (!ProjectCapabilityCatalog.IsActive(project.Type))
        {
            return false;
        }

        return TryPublish(
            previous =>
            {
                if (previous.Project?.Id != project.Id)
                {
                    return null;
                }

                return new WorkspaceSessionSnapshot(project, previous.GalleryId, NextVersion(previous));
            },
            WorkspaceSessionChangeReason.ProjectUpdated);
    }

    public bool TryEnterGallery(Guid galleryId, Guid projectId)
    {
        return TryPublish(
            previous =>
            {
                if (galleryId == Guid.Empty || previous.Project?.Id != projectId)
                {
                    return null;
                }

                return new WorkspaceSessionSnapshot(previous.Project, galleryId, NextVersion(previous));
            },
            WorkspaceSessionChangeReason.GalleryEntered);
    }

    public bool ClearGalleryIfCurrent(Guid galleryId)
    {
        return TryPublish(
            previous =>
            {
                if (previous.GalleryId != galleryId)
                {
                    return null;
                }

                return new WorkspaceSessionSnapshot(previous.Project, null, NextVersion(previous));
            },
            WorkspaceSessionChangeReason.GalleryCleared);
    }

    public bool ClearProjectIfCurrent(Guid projectId)
    {
        return TryPublish(
            previous =>
            {
                if (previous.Project?.Id != projectId)
                {
                    return null;
                }

                return new WorkspaceSessionSnapshot(null, null, NextVersion(previous));
            },
            WorkspaceSessionChangeReason.ProjectCleared);
    }

    public void Clear()
    {
        TryPublish(
            previous =>
            {
                if (previous.Project is null && previous.GalleryId is null)
                {
                    return null;
                }

                return new WorkspaceSessionSnapshot(null, null, NextVersion(previous));
            },
            WorkspaceSessionChangeReason.ProjectCleared);
    }

    public bool IsCurrent(long version)
    {
        lock (_gate)
        {
            return _current.Version == version;
        }
    }

    private static long NextVersion(WorkspaceSessionSnapshot previous)
        => checked(previous.Version + 1);

    private void Publish(
        Func<WorkspaceSessionSnapshot, WorkspaceSessionSnapshot> factory,
        WorkspaceSessionChangeReason reason)
    {
        TryPublish(previous => factory(previous), reason);
    }

    private bool TryPublish(
        Func<WorkspaceSessionSnapshot, WorkspaceSessionSnapshot?> factory,
        WorkspaceSessionChangeReason reason)
    {
        WorkspaceSessionSnapshot previous;
        WorkspaceSessionSnapshot next;
        lock (_gate)
        {
            previous = _current;
            var candidate = factory(previous);
            if (candidate is null)
            {
                return false;
            }

            next = candidate;
            _current = next;
        }

        Changed?.Invoke(this, new WorkspaceSessionChangedEventArgs(previous, next, reason));
        return true;
    }
}
