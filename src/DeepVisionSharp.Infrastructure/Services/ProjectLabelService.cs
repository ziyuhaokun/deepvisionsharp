using FreeSql;
using Microsoft.Extensions.Logging;
using DeepVisionSharp.Application.Galleries;
using DeepVisionSharp.Application.Projects;
using DeepVisionSharp.Domain.Repositories;
using DeepVisionSharp.Domain.ValueObjects;
using DeepVisionSharp.Infrastructure.Persistence;

namespace DeepVisionSharp.Infrastructure.Services;

internal sealed class ProjectLabelService : IProjectLabelService
{
    private readonly IFreeSql _db;
    private readonly IGalleryRepository _galleries;
    private readonly IAnnotationWorkflowService _annotations;
    private readonly ILogger<ProjectLabelService> _logger;

    public ProjectLabelService(
        SqliteDb sqliteDb,
        IGalleryRepository galleries,
        IAnnotationWorkflowService annotations,
        ILogger<ProjectLabelService> logger)
    {
        _db = sqliteDb.Instance;
        _galleries = galleries;
        _annotations = annotations;
        _logger = logger;
    }

    internal ProjectLabelService(
        IFreeSql db,
        IGalleryRepository galleries,
        IAnnotationWorkflowService annotations,
        ILogger<ProjectLabelService> logger)
    {
        _db = db;
        _galleries = galleries;
        _annotations = annotations;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ProjectLabelOption>> ListAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var labels = await _db.Select<SqlProjectLabel>()
            .Where(label => label.ProjectId == projectId)
            .OrderBy(label => label.Name)
            .ToListAsync(cancellationToken);

        return labels
            .Select(label => new ProjectLabelOption(label.Id, label.Name, string.IsNullOrWhiteSpace(label.Color) ? "#3B82F6" : label.Color))
            .ToArray();
    }

    public async Task<ProjectLabelOption> AddAsync(Guid projectId, string name, string color, CancellationToken cancellationToken = default)
    {
        await SqlProjectExecutionGuard.EnsureProjectAsync(_db, projectId, cancellationToken).ConfigureAwait(false);
        var normalized = name.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("标签名称不能为空。", nameof(name));
        }

        var existing = (await _db.Select<SqlProjectLabel>()
                .Where(label => label.ProjectId == projectId)
                .ToListAsync(cancellationToken))
            .FirstOrDefault(label => string.Equals(label.Name, normalized, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            return new ProjectLabelOption(existing.Id, existing.Name, string.IsNullOrWhiteSpace(existing.Color) ? color : existing.Color);
        }

        var entity = new SqlProjectLabel
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            Name = normalized,
            Color = string.IsNullOrWhiteSpace(color) ? "#3B82F6" : color
        };

        await _db.Insert(entity).ExecuteAffrowsAsync(cancellationToken);
        _logger.LogInformation("Added label {LabelName} to project {ProjectId}", entity.Name, projectId);
        return new ProjectLabelOption(entity.Id, entity.Name, entity.Color);
    }

    public async Task<IReadOnlyList<ProjectLabelOption>> AddManyAsync(
        Guid projectId,
        IReadOnlyList<ProjectLabelSeed> labels,
        CancellationToken cancellationToken = default)
    {
        await SqlProjectExecutionGuard.EnsureProjectAsync(_db, projectId, cancellationToken).ConfigureAwait(false);
        ArgumentNullException.ThrowIfNull(labels);
        if (labels.Count == 0)
        {
            return Array.Empty<ProjectLabelOption>();
        }

        var normalized = labels
            .Select(label => new ProjectLabelSeed(
                label.Name.Trim(),
                string.IsNullOrWhiteSpace(label.Color) ? "#3B82F6" : label.Color.Trim()))
            .ToArray();
        if (normalized.Any(label => string.IsNullOrWhiteSpace(label.Name)))
        {
            throw new ArgumentException("标签名称不能为空。", nameof(labels));
        }

        if (normalized.Select(label => label.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != normalized.Length)
        {
            throw new ArgumentException("批量标签中存在重复名称。", nameof(labels));
        }

        var existingNames = (await _db.Select<SqlProjectLabel>()
                .Where(label => label.ProjectId == projectId)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .Select(label => label.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var conflict = normalized.FirstOrDefault(label => existingNames.Contains(label.Name));
        if (conflict is not null)
        {
            throw new InvalidOperationException($"类别 {conflict.Name} 已存在。");
        }

        var entities = normalized.Select(label => new SqlProjectLabel
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            Name = label.Name,
            Color = label.Color
        }).ToArray();

        var affected = await _db.Insert(entities)
            .ExecuteAffrowsAsync(cancellationToken)
            .ConfigureAwait(false);
        if (affected != entities.Length)
        {
            throw new InvalidOperationException($"批量创建类别数量不一致：expected={entities.Length}, actual={affected}");
        }

        _logger.LogInformation("Added {LabelCount} labels to project {ProjectId}", entities.Length, projectId);
        return entities.Select(entity => new ProjectLabelOption(entity.Id, entity.Name, entity.Color)).ToArray();
    }

    public async Task RemoveAllAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        await SqlProjectExecutionGuard.EnsureProjectAsync(_db, projectId, cancellationToken).ConfigureAwait(false);
        await _db.Delete<SqlProjectLabel>()
            .Where(label => label.ProjectId == projectId)
            .ExecuteAffrowsAsync(cancellationToken);
        _logger.LogInformation("Removed all labels from project {ProjectId}", projectId);
    }

    public async Task<ProjectLabelOption> UpdateColorAsync(
        Guid projectId,
        Guid labelId,
        string color,
        CancellationToken cancellationToken = default)
    {
        await SqlProjectExecutionGuard.EnsureProjectAsync(_db, projectId, cancellationToken).ConfigureAwait(false);
        var entity = (await _db.Select<SqlProjectLabel>()
                .Where(label => label.ProjectId == projectId && label.Id == labelId)
                .ToListAsync(cancellationToken))
            .FirstOrDefault();

        if (entity is null)
        {
            throw new InvalidOperationException("类别不存在。");
        }

        var normalizedColor = string.IsNullOrWhiteSpace(color) ? "#3B82F6" : color.Trim();
        if (string.Equals(entity.Color, normalizedColor, StringComparison.OrdinalIgnoreCase))
        {
            return new ProjectLabelOption(entity.Id, entity.Name, entity.Color);
        }

        await _db.Update<SqlProjectLabel>()
            .Set(label => label.Color, normalizedColor)
            .Where(label => label.ProjectId == projectId && label.Id == labelId)
            .ExecuteAffrowsAsync(cancellationToken);
        entity.Color = normalizedColor;

        _logger.LogInformation(
            "Updated label color for {LabelName} in project {ProjectId} to {Color}",
            entity.Name,
            projectId,
            normalizedColor);

        return new ProjectLabelOption(entity.Id, entity.Name, entity.Color);
    }

    public async Task RemoveAsync(Guid projectId, string name, bool purgeAnnotations, CancellationToken cancellationToken = default)
    {
        await SqlProjectExecutionGuard.EnsureProjectAsync(_db, projectId, cancellationToken).ConfigureAwait(false);
        var normalized = name.Trim();
        await _db.Delete<SqlProjectLabel>()
            .Where(label => label.ProjectId == projectId && label.Name == normalized)
            .ExecuteAffrowsAsync(cancellationToken);

        if (purgeAnnotations)
        {
            await PurgeAnnotationsAsync(projectId, normalized, cancellationToken);
        }

        _logger.LogInformation("Removed label {LabelName} from project {ProjectId}", normalized, projectId);
    }

    private async Task PurgeAnnotationsAsync(Guid projectId, string labelName, CancellationToken cancellationToken)
    {
        await ForEachAnnotationDocumentAsync(projectId, async (galleryId, fileName, document) =>
        {
            var classRemoved = string.Equals(document.ImageClass, labelName, StringComparison.OrdinalIgnoreCase);
            var remainingShapes = document.Boxes
                .Where(shape => !string.Equals(shape.Label, labelName, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (!classRemoved && remainingShapes.Length == document.Boxes.Count)
            {
                return;
            }

            await _annotations.SaveAsync(
                galleryId,
                fileName,
                document.ImageWidth,
                document.ImageHeight,
                classRemoved ? null : document.ImageClass,
                remainingShapes,
                document.IsReviewed,
                cancellationToken);
        }, cancellationToken);
    }

    private async Task ForEachAnnotationDocumentAsync(
        Guid projectId,
        Func<Guid, string, AnnotationDocument, Task> action,
        CancellationToken cancellationToken)
    {
        var galleries = await _galleries.ListByProjectAsync(projectId, cancellationToken);
        foreach (var gallery in galleries)
        {
            var galleryWithSamples = await _galleries.GetSummaryByIdAsync(gallery.Id, cancellationToken);
            if (galleryWithSamples is null)
            {
                continue;
            }

            var fileNames = await _galleries.ListSampleFileNamesAsync(galleryWithSamples.Id, cancellationToken: cancellationToken);
            foreach (var fileName in fileNames)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var document = await _annotations.LoadAsync(galleryWithSamples.Id, fileName, cancellationToken);
                if (!document.HasAnnotations)
                {
                    continue;
                }

                await action(galleryWithSamples.Id, fileName, document);
            }
        }
    }
}
