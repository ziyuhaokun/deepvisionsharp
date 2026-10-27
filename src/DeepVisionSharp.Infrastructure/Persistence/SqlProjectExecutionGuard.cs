using DeepVisionSharp.Domain.ValueObjects;
using DeepVisionSharp.Application.Ports;

namespace DeepVisionSharp.Infrastructure.Persistence;

/// <summary>Enforces project availability before persistence or file mutations, including Port callers.</summary>
internal static class SqlProjectExecutionGuard
{
    public static void EnsureProject(IFreeSql db, Guid projectId)
    {
        var project = db.Select<SqlProject>().Where(item => item.Id == projectId).First()
            ?? throw new InvalidOperationException("项目不存在。");
        ProjectCapabilityCatalog.EnsureActive(project.Type);
    }

    // Path-only file Ports keep accepting external files. Protect managed project paths
    // and registered checkpoints without adding a provenance requirement to external models.
    public static void EnsurePathWritable(IFreeSql db, IProjectFileStore fileStore, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var fullPath = Path.GetFullPath(path);
        var root = Path.Combine(fileStore.DataDirectory, "projects");
        var relative = Path.GetRelativePath(root, fullPath);
        var firstSegment = relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar])[0];
        if (Guid.TryParse(firstSegment, out var projectId))
        {
            EnsureProject(db, projectId);
        }
        var model = db.Select<SqlModel>().Where(item => item.CheckpointPath == fullPath).First();
        if (model is not null)
        {
            EnsureProject(db, model.ProjectId);
        }
    }

    public static async Task EnsureProjectAsync(IFreeSql db, Guid projectId, CancellationToken cancellationToken)
    {
        var project = await db.Select<SqlProject>().Where(item => item.Id == projectId)
            .FirstAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("项目不存在。");
        ProjectCapabilityCatalog.EnsureActive(project.Type);
    }

    public static async Task EnsureGalleryAsync(IFreeSql db, Guid galleryId, CancellationToken cancellationToken)
    {
        var gallery = await db.Select<SqlGallery>().Where(item => item.Id == galleryId)
            .FirstAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("图库不存在。");
        await EnsureProjectAsync(db, gallery.ProjectId, cancellationToken).ConfigureAwait(false);
    }
}
