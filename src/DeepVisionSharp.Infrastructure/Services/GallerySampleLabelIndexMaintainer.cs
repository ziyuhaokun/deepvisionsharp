using System.Data.Common;
using FreeSql;
using DeepVisionSharp.Infrastructure.Persistence;

namespace DeepVisionSharp.Infrastructure.Services;

internal static class GallerySampleLabelIndexMaintainer
{
    public static async Task ReplaceLabelsFromDocumentAsync(
        IFreeSql db,
        Guid galleryId,
        string fileName,
        string? documentJson,
        CancellationToken cancellationToken = default,
        DbTransaction? transaction = null)
    {
        var labels = AnnotationDocumentLabelReader.ReadLabels(documentJson);
        await ReplaceLabelsAsync(db, galleryId, fileName, labels, cancellationToken, transaction);
    }

    public static async Task ReplaceLabelsAsync(
        IFreeSql db,
        Guid galleryId,
        string fileName,
        IReadOnlyList<string> labels,
        CancellationToken cancellationToken = default,
        DbTransaction? transaction = null)
    {
        var safeFileName = Path.GetFileName(fileName);
        var delete = db.Delete<SqlSampleLabelIndex>()
            .Where(item => item.GalleryId == galleryId && item.FileName == safeFileName);
        if (transaction is not null)
        {
            delete = delete.WithTransaction(transaction);
        }

        await delete.ExecuteAffrowsAsync(cancellationToken);
        if (labels.Count == 0)
        {
            return;
        }

        var entities = labels
            .Select(label => label.Trim())
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(label => new SqlSampleLabelIndex
            {
                GalleryId = galleryId,
                FileName = safeFileName,
                Label = label
            })
            .ToArray();

        if (entities.Length == 0)
        {
            return;
        }

        var insert = db.Insert(entities);
        if (transaction is not null)
        {
            insert = insert.WithTransaction(transaction);
        }

        await insert.ExecuteAffrowsAsync(cancellationToken);
    }

    public static async Task DeleteSampleAsync(
        IFreeSql db,
        Guid galleryId,
        string fileName,
        CancellationToken cancellationToken = default,
        DbTransaction? transaction = null)
    {
        var safeFileName = Path.GetFileName(fileName);
        var delete = db.Delete<SqlSampleLabelIndex>()
            .Where(item => item.GalleryId == galleryId && item.FileName == safeFileName);
        if (transaction is not null)
        {
            delete = delete.WithTransaction(transaction);
        }

        await delete.ExecuteAffrowsAsync(cancellationToken);
    }

    public static async Task MoveSampleAsync(
        IFreeSql db,
        Guid sourceGalleryId,
        Guid targetGalleryId,
        string sourceFileName,
        string targetFileName,
        string? documentJson,
        CancellationToken cancellationToken = default,
        DbTransaction? transaction = null)
    {
        var safeSourceFileName = Path.GetFileName(sourceFileName);
        var safeTargetFileName = Path.GetFileName(targetFileName);
        var select = db.Select<SqlSampleLabelIndex>()
            .Where(item => item.GalleryId == sourceGalleryId && item.FileName == safeSourceFileName);
        if (transaction is not null)
        {
            select = select.WithTransaction(transaction);
        }

        var rows = await select.ToListAsync(cancellationToken);

        await DeleteSampleAsync(db, sourceGalleryId, safeSourceFileName, cancellationToken, transaction);
        await DeleteSampleAsync(db, targetGalleryId, safeTargetFileName, cancellationToken, transaction);

        if (rows.Count > 0)
        {
            var entities = rows
                .Select(row => new SqlSampleLabelIndex
                {
                    GalleryId = targetGalleryId,
                    FileName = safeTargetFileName,
                    Label = row.Label
                })
                .ToArray();

            var insert = db.Insert(entities);
            if (transaction is not null)
            {
                insert = insert.WithTransaction(transaction);
            }

            await insert.ExecuteAffrowsAsync(cancellationToken);
            return;
        }

        if (!string.IsNullOrWhiteSpace(documentJson))
        {
            await ReplaceLabelsFromDocumentAsync(
                db,
                targetGalleryId,
                safeTargetFileName,
                documentJson,
                cancellationToken,
                transaction);
        }
    }

    public static async Task DeleteGalleryAsync(
        IFreeSql db,
        Guid galleryId,
        CancellationToken cancellationToken = default,
        DbTransaction? transaction = null)
    {
        var delete = db.Delete<SqlSampleLabelIndex>().Where(item => item.GalleryId == galleryId);
        if (transaction is not null)
        {
            delete = delete.WithTransaction(transaction);
        }

        await delete.ExecuteAffrowsAsync(cancellationToken);
    }

    public static async Task BackfillMissingAsync(IFreeSql db, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT a.Id, a.GalleryId, a.FileName, a.Width, a.Height, a.ImageData, a.DocumentJson
            FROM Annotations a
            WHERE NOT EXISTS (
                SELECT 1
                FROM SampleLabelIndex i
                WHERE i.GalleryId = a.GalleryId AND i.FileName = a.FileName
            )
            """;

        var missingAnnotations = await db.Ado.QueryAsync<SqlAnnotation>(sql, cancellationToken);
        var activeProjects = (await db.Select<SqlProject>().ToListAsync(cancellationToken))
            .Where(project => DeepVisionSharp.Domain.ValueObjects.ProjectCapabilityCatalog.IsActive(project.Type))
            .Select(project => project.Id).ToHashSet();
        var activeGalleries = (await db.Select<SqlGallery>().ToListAsync(cancellationToken))
            .Where(gallery => activeProjects.Contains(gallery.ProjectId))
            .Select(gallery => gallery.Id).ToHashSet();
        foreach (var annotation in missingAnnotations)
        {
            if (!activeGalleries.Contains(annotation.GalleryId))
            {
                continue;
            }
            await ReplaceLabelsFromDocumentAsync(
                db,
                annotation.GalleryId,
                annotation.FileName,
                annotation.DocumentJson,
                cancellationToken);
        }
    }
}
