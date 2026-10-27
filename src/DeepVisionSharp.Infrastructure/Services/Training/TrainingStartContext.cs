using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Application.Training;
using DeepVisionSharp.Domain.Entities;

namespace DeepVisionSharp.Infrastructure.Services;

internal sealed record TrainingStartValidationContext(
    StartTrainingJobCommand Command,
    IReadOnlyList<Guid> ParticipatingGalleryIds,
    IReadOnlyList<ProjectGallery> Galleries,
    IReadOnlyDictionary<Guid, int> AnnotatedCounts);

internal sealed record TrainingStartContext(
    StartTrainingJobCommand Command,
    TrainingJob Job,
    TrainingWorkspacePlan WorkspacePlan,
    IReadOnlyList<ProjectGallery> Galleries,
    IReadOnlyDictionary<Guid, int> AnnotatedCounts,
    IReadOnlyList<Guid> ParticipatingGalleryIds,
    List<string> InformationalMessages);

internal sealed record TrainingBackendStartResult(
    TrainingJob Job,
    string ModelType,
    string DataRoot,
    string OutputRoot,
    string LogFile,
    IReadOnlyList<string> InformationalMessages);
